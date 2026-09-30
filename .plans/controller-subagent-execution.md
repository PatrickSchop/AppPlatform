# Autonomous execution: controller and sub-agents

How to execute a large plan by splitting it into tasks and running each through a fixed pipeline of Claude Code CLI sub-agents, driven by one interactive controller session.

- **Controller**: the interactive Claude Code session (VS Code), model **Sonnet**. It does no project work.
- **Sub-agents**: separate `claude -p` processes, started only by the controller through the Bash tool. Sub-agents never talk to each other. All hand-offs go through files that the controller reads and writes.

This document is the controller's operating manual. Read it fully before starting, and re-read §3 and §9 after any context compaction.

---

## 1. Roles at a glance

| Role | Model | May change files | May run commands | Purpose |
|---|---|---|---|---|
| Controller | sonnet | only `.plans/**` bookkeeping | dispatch only (see §4) | Reads the plan, dispatches, tracks progress, handles the user |
| Planner | opus | no | read-only | Plans and re-plans; decides on splitting, escalation and next action |
| Executor | haiku (escalates to sonnet, rarely opus) | yes | yes (build, test) | All real work. **Never** commits, pushes, deploys |
| Status reviewer | haiku | no | read-only + build/test | Independently verifies that the intended end state is reached |
| Code reviewer | sonnet | no | read-only (`git diff`) | Advises on further changes. Runs only at the end of the full implementation or on explicit request. Good enough is fine |
| Publisher | haiku | no | git, gh | Commit, push, wait for CI, report |

---

## 2. Files and layout

Given a plan `.plans/<plan>.md`:

```
.plans/<plan>.md                     the large plan (input, read-only for the controller unless the user asks)
.plans/<plan>-progress.md            overall progress (controller updates after every task; existing convention)
.plans/<plan>/step-<ID>-*.md         task documents, if the plan has them
.plans/<plan>/runs/<ID>/             per-task working directory, created by the controller
    01-plan.prompt.md   01-plan.report.md      planner
    02-exec-a1.prompt.md 02-exec-a1.report.md  executor, attempt 1  (a2, a3, ...)
    03-verify-a1.prompt.md 03-verify-a1.report.md   status reviewer (once, after the whole step)
    (code review is not per step: it lives in `.plans/<plan>/runs/FINAL/` or `runs/REVIEW-<n>/`, see §5.4)
    05-publish.prompt.md 05-publish.report.md  publisher (once, after verification)
    *.json                                     raw CLI output of each run (for cost, session id, errors)
```

Number files in the order they are created; attempts get `a1`, `a2`, ... suffixes. A sub-agent's **report file is the source of truth**. The stdout JSON is only used for the exit status, cost and errors.

Keep prompts and reports as files (not inline strings): quoting is fragile on Windows, prompts can be long, and the files make the run auditable and restartable.

---

## 3. The controller loop

Continue autonomously until one of the stop conditions in §8 is met. Do not ask the user for confirmation between tasks.

**Terms.** A **step** is one task of the plan (for example MT-08). The planner may split a step into **sub-steps**. Sub-steps are executed one after another, but together they are still **one step**: the status reviewer and the publisher run **once per step**, after its last sub-step, not after each sub-step.

The pipeline is **not a straight line**. The planner is the hub. Every run ends with a report, and the controller applies one rule after each report:

> **If the report says the work is finished as far as that run can tell, go to the next run. In every other case (partial, failed, blocked, timeout, not achieved, CI failed, dependency found), go back to the planner with the reports, and let the planner decide what happens next.**

```
                  ┌────────────────────────────────────────────┐
                  │                                            │ any outcome other than "finished"
                  ▼                                            │
 next step ──▶ PLANNER ──▶ EXECUTOR ──▶ (more sub-steps?) ──▶ STATUS ──▶ PUBLISHER ──▶ step done ──▶ next step
                  ▲ (brief)   (work)        │ yes: next          REVIEWER   (commit, CI)                  │
                  │                         │ sub-step executor      │            │                       ▼
                  └─────────────────────────┴────────────────────────┴────────────┘          all steps done: CODE REVIEW (once)
                                                                                                 │ `must` items → planner → fix step
```

Steps for a step T:

1. **PLAN**: the planner reads T and the codebase and returns a brief (§5.1). Its decision is one of: execute, split T into sub-steps (an ordered list in the brief; T stays one step in the progress file, with its sub-steps listed under it), reorder (insert a prerequisite before T), needs user, abort.
2. **EXECUTE**: the executor does the brief, or the next sub-step of it, at the model the planner chose (§5.2).
   - Finished and more sub-steps remain → **EXECUTE** the next sub-step. No verification or publishing yet. The planner is not called in between, unless the brief says so.
   - Finished and it was the last sub-step (or T was not split) → step 3.
   - Anything else (partial, failed, blocked, failure loop, timeout, dependency found) → **PLAN**.
3. **VERIFY**: the status reviewer checks the end state of the **whole step** against its acceptance criteria (§5.3).
   - Achieved → step 4.
   - Not achieved → **PLAN**.
4. **PUBLISH**: the publisher commits **all** changes of the step in one commit, pushes, and waits for CI (§5.5).
   - Pushed and CI passed (or no CI) → step 5.
   - Any failure → **PLAN**, with the CI details from the report.
5. **RECORD**: update the progress file. T is done. Pick the next step.

**Code review is not part of this loop.** It runs in two cases only (§5.4): once at the end of the full implementation, when all steps are done, and whenever the user or the plan explicitly asks for it.

When the planner is called again it always gets: the step, all earlier reports of this step in `runs/<ID>/`, and the attempt counters. It answers the same question each time: *what is still needed to reach the acceptance criteria?* Its answer can be another executor run (same or higher model, different approach), a split or a changed sub-step list, a reorder, a requirement for the user, or an abort. After a re-plan the step continues from EXECUTE. VERIFY and PUBLISH always run again after new changes, since earlier verification no longer applies to the changed code.

Rules:

- **Sequential.** Start a step only when the previous one is fully done (verified, published, CI green, progress updated). Sub-steps of the same step run in order, without verification and publishing in between.
- **Exception: dependency discovered mid-task.** When a report says T needs something that is not done (a missing prerequisite task, a missing decision), pause T, ask the planner to re-plan (insert the prerequisite before T, or reorder), then continue. Note this in the progress file.
- **One agent at a time.** Never run two sub-agents concurrently. Working tree conflicts are the main risk in this setup.
- **The controller never does the work itself**, not even a small fix. If something is small, still dispatch it (to the executor via the planner).
- **Working tree hygiene.** Before each task run `git status --short` (allowed for the controller, read-only). It must be clean apart from `.plans/**`. If not, treat it as a user-action stop unless the progress file explains the changes.

### Attempt and escalation limits

Prevent infinite loops. All counters are per task and recorded in the progress file.

| Situation | Limit | When exceeded |
|---|---|---|
| Executor attempts at the same model | 2 | Planner must escalate the model or change the approach |
| Executor attempts in total | 5 | Stop the run: quality not reachable (§8) |
| Model ladder | haiku → sonnet → opus | **Attempt 1 is always haiku** (§12.2). Escalation is decided by the planner, with a stated reason. Opus is for rare, hard problems |
| Final-review change rounds | 1 | Fix the `must` items once (as a fix step), then accept if verification passes (good enough) |
| Publish attempts (CI failure) | 3 | Stop the run |

---

## 4. What the controller may and may not do

May:
- Read any file. Run read-only git (`git status`, `git log`, `git diff --stat`).
- Write and update files under `.plans/` (prompts, run directory, progress file).
- Start sub-agents through the command in §6.
- Talk to the user.

May not:
- Edit source code, run builds/tests, commit, push, or deploy.
- Start sub-agents through the `Agent` tool. Sub-agents are **CLI processes** so that each one gets a chosen model, tool restrictions and a hard timeout.

---

## 5. Sub-agent specifications

Every sub-agent prompt is written by the controller into the `*.prompt.md` file and must contain, in this order:

1. **Role and hard limits** (copy the role block below).
2. **Context**: repository root, plan path, task id, pointers to the task document and to the reports of earlier steps in `runs/<ID>/`. Point to files, do not paste them.
3. **The specific assignment** for this run (from the planner's brief when there is one).
4. **Output contract**: the report file to write and the exact report format from §7.
5. **Stop rules** that apply to this role.

Also tell each agent to read `CLAUDE.md` in the repository root if it exists (the CLI loads it automatically, but say so anyway).

### 5.1 Planner (opus)

- **Job**: Before any execution, read the task and the relevant code, and produce an execution brief. When called again (re-planning) read the latest reports and adjust.
- **Decides**: whether the step must be split (then returns an ordered list of sub-steps, each small enough for one executor run of at most 15 minutes, the executor's own limit, and the acceptance criteria for the step as a whole; sub-steps are verified and published together), which executor model to use (**haiku by default**, and always for the first attempt. Sonnet only after a haiku attempt failed for a reason the planner names, opus only for rare hard problems. The brief must be detailed enough for haiku, see §12.2; and how heavy verification is, see §12.4), which prerequisite is missing, and what "done" means as checkable acceptance criteria.
- **Never** edits files (except its own report file) and never runs build or test commands.
- **Report contains**: `DECISION` (`EXECUTE` | `SPLIT` | `REORDER` | `NEEDS_USER` | `ABORT`), the executor model, the ordered work list, acceptance criteria as a checklist with the exact commands that prove each one, known risks, and on re-plan an analysis of why the previous attempt fell short.
- **Re-plan triggers**: failed or partial executor report, status reviewer says not achieved, publisher reports a failure, a dependency appeared, final code review has `must` items.

### 5.2 Executor (haiku, escalated on the planner's word)

- **Job**: Perform exactly the work in the brief: code changes, builds, tests.
- **Must not**: `git commit`, `git push`, `git tag`, `git reset --hard`, `git checkout -- <file>` on files it did not create, deploy, publish packages, or change anything outside the repository.
- **Stops and reports (does not improvise)** when:
  - user interaction is required (missing secret, a decision, a login, an ambiguous requirement),
  - it is in a **failure loop**: the same test or build error persists after three attempted fixes, or fixes visibly have no effect,
  - its **own time limit of 15 minutes** is reached, whatever the status. The agent enforces this itself so it can still write a full report. At the very start it records the time (`date +%s` in Bash to a file `runs/<ID>/<NN>-exec-aN.start`), and re-checks the elapsed time (`date +%s`) before starting each new step and after each build or test run. At 12 minutes it starts no new work item. At 15 minutes it stops, and uses the remaining time to write the report with `STATUS: TIMEOUT`. It also runs long commands (builds, tests) with their own `timeout` (at most 5 minutes each) so one command cannot eat the whole budget. The controller's hard timeout (§6) is a backstop that fires later, at 20 minutes, and should normally never trigger.
- **Always writes an extensive report** (§7), including partial work, exact error output, what was tried, and the current state of the working tree. It writes an interim report file after each major step, so that a hard kill still leaves a usable trace.
- Runs the build and the test suite it was told to run and includes the actual results, not a claim.

### 5.3 Status reviewer (haiku)

- **Job**: Runs **once per step**, when the executor has finished the last sub-step. Independently verifies that the intended end state is achieved, using the planner's acceptance criteria. Re-runs the verifying commands itself. Trusts nothing from the executor report.
- **Read-only**: no edits. May run build/test/inspection commands.
- **Report**: for each acceptance criterion `MET` / `NOT MET` / `UNVERIFIABLE` with the evidence (command and output excerpt), plus an overall `ACHIEVED` | `NOT_ACHIEVED`, and a list of what is missing.
- The controller does not overrule the reviewer. Not achieved → re-plan (§3).

### 5.4 Code reviewer (sonnet)

- **Not run per step.** The controller starts it only:
  1. **Once at the end of the full implementation**, after the last step is published. Run directory `runs/FINAL/`.
  2. **On explicit request**: the user asks for it, or the plan or a step document says so. Run directory `runs/REVIEW-<n>/`, scoped to what was asked for.
- Never start it on the controller's or planner's own initiative in between.
- **Job**: Review the changes in scope (`git diff <first-commit>..HEAD` for the final review, using the commit hashes in the progress file, or the scope that was requested). For a large diff, give the reviewer the per-step commit list and let it review by area. Look for correctness bugs, missing tests, security issues, and breaks of the surrounding code's conventions.
- **Read-only**. Does not change code.
- **Report**: `ADVICE` = `ACCEPT` | `CHANGE`. Under `CHANGE` list only the changes that are worth it, each with file, reason and severity (`must` | `should`). **Good enough is fine**: no nitpicks, no style preferences, no speculative refactors. Only `must` items trigger work: the controller hands them to the planner, who defines a **fix step** (executor, status reviewer and publisher as for any step). The code reviewer does not run again after that. `should` items are recorded in the progress file and do not block.

### 5.5 Publisher (haiku)

- **Job**: Runs **once per step**, after successful verification. Commit and push the step's changes (all sub-steps together, one commit), wait for CI, report.
- Uses the commit message convention of the repository (look at `git log`) and the attribution lines the controller passes in the prompt. Commits only the files belonging to this task. Never force-pushes. Never amends earlier commits.
- **CI**: if the project has CI (look for `.github/workflows/`, or ask `gh run list`), wait for the run for the pushed commit to finish (`gh run watch <id> --exit-status`, with a limit of 20 minutes). If there is no CI, say so in the report.
- **Report**: commit hash, branch, push result, CI status (`PASSED` | `FAILED` | `NONE` | `TIMEOUT`). On failure it **must** include enough detail to plan a fix: failing job and step names, the relevant log excerpt (`gh run view <id> --log-failed`), and the files/tests involved. On a CI failure the task is **not finished**.

---

## 6. How to start a sub-agent

Sub-agents run as `claude -p` (print mode: non-interactive, exits when done). The controller runs them with the **Bash** tool (Git Bash, POSIX syntax).

### Command template

```bash
cd /c/Dev/AppPlatform   # repository root
RUN=.plans/<plan>/runs/<ID>

env -u CLAUDECODE -u CLAUDE_CODE_CHILD_SESSION -u CLAUDE_CODE_SESSION_ID \
    -u CLAUDE_CODE_MESSAGING_SOCKET -u CLAUDE_CODE_MESSAGING_TOKEN \
    -u CLAUDE_CODE_SESSION_ATTENDED -u CLAUDE_CODE_ENTRYPOINT \
  timeout <SECONDS> claude -p \
  --model <haiku|sonnet|opus> \
  --output-format json \
  --permission-mode <mode> \
  --allowedTools <...> \
  --disallowedTools <...> \
  < "$RUN/<NN>-<name>.prompt.md" \
  > "$RUN/<NN>-<name>.json" 2> "$RUN/<NN>-<name>.err"
echo "exit=$?"
```

- **Nested-session guard.** The controller is itself a Claude Code session, so its environment contains `CLAUDECODE=1`, and a child `claude` refuses to start ("cannot be launched inside another Claude Code session"). That run exits with code 0 but `is_error: true` in the JSON and produces no report. The `env -u ...` prefix removes that variable and the ones that tie a child to the parent session (session id, messaging socket and token, entrypoint), so each sub-agent is an independent process. Always use the prefix. Verified on 2026-09-30 with `--model haiku` and `--model opus`.
- The prompt goes in through **stdin** from the prompt file. Do not pass long prompts as an argument.
- `-p` never prompts for permissions. A tool that is not allowed is simply denied, so the allow-list must cover what the role needs (see the table). Do **not** use `--dangerously-skip-permissions`.
- `--output-format json` prints one JSON object with `result`, `is_error`, `session_id`, `num_turns` and `total_cost_usd`. Parse it to detect failed runs.
- `--model` accepts the aliases `haiku`, `sonnet`, `opus`.
- Optional: `--max-budget-usd <n>` as a cost safety net per run.
- Check `claude --help` first if a flag is rejected. Flags differ between versions.

### Timeouts

- The Bash tool call itself is limited to 10 minutes. Executor runs (up to 20 min) **must** be started with `run_in_background: true`. The controller is notified when the process exits. Do not poll or sleep.
- Wrap every run in `timeout`: executor `1200` (20 min: the agent's own 15-minute limit plus 5 minutes of margin for writing its report), planner `600`, status reviewer `600`, code reviewer `600`, publisher `1800` (includes waiting for CI).
- **The agent's own time limit comes first.** A hard timeout must always be later than the limit the agent applies to itself, so the agent can stop cleanly and write its report. The controller's timeout is only a backstop for a hung or runaway process. The same holds for every role: the prompt states the agent's own limit (planner, reviewers: 8 minutes; executor: 15 minutes; publisher: 25 minutes including CI), which is always below the hard `timeout`.
- Exit code `124` means the hard timeout fired, so the agent did not stop by itself. Read whatever interim report it managed to write, mark the run `TIMEOUT` in the progress file, and re-plan (the planner should consider splitting the task).
- If `timeout` leaves orphaned child processes (dev servers, test hosts), have the next step's prompt start with a check for leftovers, or list them in the progress file for the user.

### Per-role settings

| Role | `--model` | `--permission-mode` | `--allowedTools` | `--disallowedTools` | `timeout` |
|---|---|---|---|---|---|
| Planner | opus | `default` | `Read Glob Grep` | `Edit Write Bash` | 600 |
| Executor | haiku (default), sonnet or opus per brief | `acceptEdits` | `Read Glob Grep Edit Write Bash` | `Bash(git commit:*) Bash(git push:*) Bash(git tag:*) Bash(git reset:*) Bash(git rebase:*) Bash(git merge:*) Bash(gh:*)` | 1200 |
| Status reviewer | haiku | `default` | `Read Glob Grep Bash` | `Edit Write Bash(git commit:*) Bash(git push:*)` | 600 |
| Code reviewer | sonnet | `default` | `Read Glob Grep Bash(git diff:*) Bash(git log:*) Bash(git show:*) Bash(git status:*)` | `Edit Write` | 600 |
| Publisher | haiku | `acceptEdits` | `Read Glob Grep Write Bash(git:*) Bash(gh:*)` | `Bash(git push --force:*) Bash(git push -f:*) Bash(git reset:*)` | 1800 |

Notes on the table:
- Tool-rule syntax (`Bash(git diff:*)`, `Write(.plans/**)`) depends on the CLI version. If a rule is not honored, verify with a trivial dry-run prompt before the real run, and tighten with instructions in the prompt as a second line of defense.
- The executor needs the build and test commands of the project in `Bash`. If the project needs a narrower allow-list, list the exact commands (for example `Bash(dotnet:*)`, `Bash(npm:*)`).
- Reviewers write only their report file. Their prompt names the exact path.

### Executor model escalation

The planner names the model in its brief. To escalate, the controller only changes `--model` (and the run attempt number). It does not decide this itself.

### Reports of read-only roles

Planner, status reviewer and code reviewer have no write access. (The `Write(.plans/**)` rule was not honored by the CLI in the MT-08 run, and the planner could not save its report.) Remove `Write(...)` from their allow-lists. They write the report **as their final message**, and the controller saves the `result` field of the JSON as the report file:

```bash
python -c "import json,sys; print(json.load(open(sys.argv[1],encoding='utf-8'))['result'])" "$RUN/<NN>-<name>.json" > "$RUN/<NN>-<name>.report.md"
```

The executor and publisher write their own report files, because they must also leave interim reports.

### After each run the controller

1. Checks the exit code (`0` normal, `124` timeout, other = CLI failure).
2. Reads `*.json` for `is_error`. On an API/CLI error (rate limit, auth, overload) retry the identical run once after a short wait. If it fails again, stop with a user-action stop (§8).
3. Checks that the report file exists. If missing, use the `result` field of the JSON as the report and note that in the progress file. If both are empty treat the run as failed.
4. Reads **only the header of the report** (the first lines up to `DIGEST:`, for example with `head -5`) and decides the next step according to §3. The full report is opened only when the header is unclear or the run ended in `NEEDS_USER`. Otherwise the full report goes to the planner by **path**, not through the controller's context (§12.1).
5. Appends the run's cost line to the progress file (§12.5).

---

## 7. Report format

Every sub-agent writes its report to the given path in this shape, so the controller can parse it at a glance:

```markdown
# <role> report: <task id>, <attempt>

STATUS: COMPLETE | PARTIAL | BLOCKED | FAILED | NEEDS_USER | TIMEOUT
OUTCOME_ACHIEVED: yes | no | unknown
DIGEST: one line, at most 200 characters: what happened and what the next step should be

## Summary
Two to five sentences.

## Work done
- Files changed (path, one-line reason)
- Commands run and their results (build, tests: counts and failures)

## Problems
- Exact error output (trimmed to the relevant part), what was tried, why it did not work
- Any failure loop, dependency, or user decision needed

## Current state
- Working tree state (`git status --short`)
- What is left to do

## Recommendation
What the next step should be, and for the planner: whether to change model or approach.
```

The status reviewer, code reviewer, planner and publisher add their role-specific sections (§5) below `Current state`.

---

## 8. Stop conditions for the controller

The controller keeps going until one of these applies. Then it stops, updates the progress file, and gives the user a clear message.

| Stop | Trigger | Message must contain |
|---|---|---|
| **Finished** | Every step is complete and published, and the final code review is done and its `must` items are fixed | Summary per step, commits, open `should` review items |
| **User action needed** | A sub-agent returned `NEEDS_USER`, the planner returned `NEEDS_USER`, the working tree is unexpectedly dirty, credentials or CLI/API errors persist, or a decision outside the plan is required | The exact question or action, the task, where the reports are, how to resume |
| **Quality not reachable** | Attempt limits in §3 exceeded, the planner returns `ABORT`, or repeated failure loops persist even after escalation to opus | What was tried per attempt, the last errors, the planner's analysis, and a suggested manual next step |

Never continue past a stop by lowering the acceptance criteria. Never skip a failing task to do a later one, unless the planner explicitly reorders.

---

## 9. Progress tracking

The controller owns `.plans/<plan>-progress.md` and updates it after **every** sub-agent run, not only at task end. It survives context loss, so it must be enough to resume.

Per step keep:

- State: `not started` | `planning` | `executing (sub-step i of n, attempt n, model)` | `verifying` | `publishing` | `done` | `blocked`
- Attempt counters (executor per model, review rounds, publish attempts)
- Start commit hash, final commit hash, CI result
- Sub-steps with their state (when the planner split the step)
- Path to `runs/<ID>/` and a one-line note of the latest report's outcome

At the top: date, tasks done of total, current task, and the **next action** in one line (for example "run sub-step 2 of 3 executor on MT-08").

**Resuming** (new controller session, or after compaction): read this document, the plan, the progress file, the latest report in the current run directory, then continue at the recorded next action. If a sub-agent was running when the session ended, check for its `.json` output and report before starting anything new, and check `git status`.

---

## 10. Writing good prompts (controller checklist)

- Self-contained: the sub-agent has no memory of earlier runs. Point to files instead of summarizing from memory.
- One clear assignment per run. Small enough for 15 minutes. State the agent's own time limit in the prompt (§6).
- State the acceptance criteria and the exact commands that prove them.
- State what the agent must **not** do (commit, push, touch unrelated files, expand scope).
- State the report path and refer to the format in §7. Copy the format into the prompt, since the agent does not see this document unless told to.
- On a retry, include the previous report path and the planner's analysis. Never just repeat the same prompt after a failure.
- Do not include the user's email or secrets in prompts.

---

## 11. Quick start for the controller

1. Read this document, the plan, and the progress file.
2. Create `.plans/<plan>/runs/<ID>/` for the first unfinished task.
3. Write the planner prompt, run it (§6), read the brief.
4. Run the pipeline of §3 for the task, updating the progress file after every run.
5. Repeat with the next task until a stop condition of §8 is met.

---

## 12. Token efficiency

Measured on the MT-08 run (18 sub-agent runs, about 16 USD; 66% of usage at more than 150k context; 62% in subagent-heavy sessions):

| Role | Share of sub-agent cost | Observation |
|---|---|---|
| Executor | about 60% | Every executor ran on **sonnet**, because the planner chose it. 83 and 87 turns, about 40k tokens of context per turn |
| Code reviewer | about 23% | Ran after every executor attempt. 43 turns in the worst run, for a 2.6 KB advice. Now once, at the end |
| Planner | about 11% | Opus, three runs |
| Status reviewer and publisher | about 6% | Haiku |

The rules below are mandatory for the controller and the planner.

### 12.1 Keep the controller's context small

The controller is one long session, so its context is the most expensive part of the run: every turn re-reads all of it.

- **Read report headers only** (§6, after each run). Pass full reports to the planner by path.
- **Never read source code, diffs or logs.** That is the sub-agents' work.
- **Read the task document and the progress file, not the whole plan.** Open the large plan only for the section the task names.
- **No commentary.** Between runs write one line of status at most. Do not restate reports to the user.
- **Checkpoint per task.** After a task is `done` and recorded, the progress file holds everything needed (§9). Run `/compact` (or `/clear`, then follow "Resuming" in §9) when the context passes about 100k tokens, or every third task, whichever comes first. Never carry a finished task's details into the next.
- Prompts are files and are short (§10). Refer to report paths, do not paste content.

### 12.2 Haiku does the implementation, the plan makes that possible

Most executor cost comes from exploring the code base. Haiku is fine when the planner has already done the exploring. Therefore:

- The planner's brief for an executor is **concrete**: the exact files to create or change, the type and method signatures, the patterns to copy (file and line of an existing example), the targeted build and test commands, and the order of the work. It never says "figure out where". If the planner cannot be that concrete, it splits the task until it can.
- **Attempt 1 is always haiku.** The planner may pick sonnet for a first attempt only for a named reason (for example a subtle concurrency or migration problem) and writes that reason in the brief. Opus executors are for rare cases after sonnet failed.
- On failure the planner chooses between (a) a sharper brief with haiku, (b) sonnet, (c) a split. Prefer (a) and (c) before (b): a failed attempt usually means the brief was unclear, not that the model was weak.
- Size limit per executor run: about 6 to 10 files touched and a budget of about 40 tool calls. Put both in the prompt. Above that, split.

### 12.3 Review and verify once

- **Code review**: once at the end of the full implementation, or on explicit request (§5.4). Never per step.
- **Status reviewer and publisher**: once per step, not per sub-step or attempt. Sub-steps of a split step share one verification and one commit. This also avoids a CI run per sub-step.
- Give the code reviewer the scope in the prompt (per-step commit list and paths) and a budget of about 40 tool calls for the final review. It must not wander through the whole repository.
- The reviewer stays sonnet and reports `must` items only (§5.4).

### 12.4 Keep every sub-agent run lean

- **Targeted commands.** Executors build and test only the affected projects and tests (for .NET: `dotnet test <project> --filter <name>`) and run the full suite **once**, at the end. The status reviewer runs the full suite once, plus the checks in the acceptance list.
- **Read narrowly.** Prompts tell agents to read only the files named in the brief, to `Grep` before reading whole files, and to read large files by line range.
- **Trim build output.** Redirect long build and test output to a file and read only the failure lines (`grep -E "error|Failed"`, `tail -40`).
- **Light verification.** The planner can set `VERIFY: light` for steps with nothing verifiable beyond a green build and tests. The status reviewer then only re-runs the build and the tests.
- **No session reuse.** Do not use `--continue` or `--resume` for sub-agents. Every run starts fresh with a small prompt. A reused large context costs more than a new brief.

### 12.5 Measure

After each run the controller appends one line to the progress file, taken from the JSON output:

```bash
python -c "import json,sys; d=json.load(open(sys.argv[1],encoding='utf-8')); print(sys.argv[1], ','.join(d.get('modelUsage',{})), 'turns', d['num_turns'], 'usd', round(d['total_cost_usd'],2))" "$RUN/<NN>-<name>.json"
```

The progress file then shows the cost per task. When a task costs more than about 2 times the median, the planner reviews at the next re-plan why (brief too vague, task too large, too many review rounds).

Targets: at least 80% of executor runs on haiku; one code review per plan (at the end); controller context under 100k at the start of every task.
