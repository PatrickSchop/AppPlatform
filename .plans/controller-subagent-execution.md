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
| Code reviewer | sonnet | no | read-only (`git diff`) | Advises on further changes. Good enough is fine |
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
    03-verify-a1.prompt.md 03-verify-a1.report.md   status reviewer
    04-review-a1.prompt.md 04-review-a1.report.md   code reviewer
    05-publish.prompt.md 05-publish.report.md  publisher
    *.json                                     raw CLI output of each run (for cost, session id, errors)
```

Number files in the order they are created; attempts get `a1`, `a2`, ... suffixes. A sub-agent's **report file is the source of truth**. The stdout JSON is only used for the exit status, cost and errors.

Keep prompts and reports as files (not inline strings): quoting is fragile on Windows, prompts can be long, and the files make the run auditable and restartable.

---

## 3. The controller loop

Continue autonomously until one of the stop conditions in §8 is met. Do not ask the user for confirmation between tasks.

The pipeline is **not a straight line**. The planner is the hub. Every step ends with a report, and the controller applies one rule after each report:

> **If the report says the task is finished as far as that step can tell, go to the next step. In every other case (partial, failed, blocked, timeout, not achieved, changes advised, CI failed, dependency found), go back to the planner with the reports, and let the planner decide what happens next.**

```
                      ┌───────────────────────────────────────────────┐
                      │                                               │
                      ▼                                               │ any outcome other than
  pick next task ──▶ PLANNER ──▶ EXECUTOR ──▶ STATUS ──▶ CODE ──▶ PUBLISHER ──▶ done ──▶ next task
                      ▲  (brief)    (work)     REVIEWER  REVIEWER   (commit, CI)          │ "finished"
                      │                          │          │          │                   │
                      └──────────────────────────┴──────────┴──────────┴───────────────────┘
                              unfinished / failed / advice to change / CI failed / dependency
```

Steps for the task T:

1. **PLAN**: the planner reads T and the codebase and returns a brief (§5.1). Its decision is one of: execute, split T into sub-tasks (the controller replaces T by them in the progress file and continues with the first), reorder (insert a prerequisite before T), needs user, abort.
2. **EXECUTE**: the executor does the brief, at the model the planner chose (§5.2).
   - Finished → step 3.
   - Anything else (partial, failed, blocked, failure loop, timeout, dependency found) → **PLAN**.
3. **VERIFY**: the status reviewer checks the end state (§5.3).
   - Achieved → step 4.
   - Not achieved → **PLAN**.
4. **REVIEW**: the code reviewer inspects the diff (§5.4).
   - Accept, or only `should` items → step 5.
   - `must` changes → **PLAN**.
5. **PUBLISH**: the publisher commits, pushes and waits for CI (§5.5).
   - Pushed and CI passed (or no CI) → step 6.
   - Any failure → **PLAN**, with the CI details from the report.
6. **RECORD**: update the progress file. T is done. Pick the next task.

When the planner is called again it always gets: the task, all earlier reports of this task in `runs/<ID>/`, and the attempt counters. It answers the same question each time: *what is still needed to reach the acceptance criteria?* Its answer can be another executor run (same or higher model, different approach), a split, a reorder, a requirement for the user, or an abort. After a re-plan the task continues through the steps again from EXECUTE. Steps 3 to 5 always run again after new changes, since earlier verification and review no longer apply to the changed code.

Rules:

- **Sequential.** Start a task only when the previous one is fully done (published, CI green, progress updated).
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
| Model ladder | haiku → sonnet → opus | Escalation is decided by the planner. Opus is for rare, hard problems |
| Review-driven change rounds | 2 | Accept the result if verification passes (good enough) |
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
- **Decides**: whether the task must be split (then returns the sub-tasks, each small enough for one executor run of at most 15 minutes, the executor's own limit), which executor model to use (default haiku; sonnet when the change is subtle or cross-cutting or haiku already failed; opus only for rare hard problems), which prerequisite is missing, and what "done" means as checkable acceptance criteria.
- **Never** edits files (except its own report file) and never runs build or test commands.
- **Report contains**: `DECISION` (`EXECUTE` | `SPLIT` | `REORDER` | `NEEDS_USER` | `ABORT`), the executor model, the ordered work list, acceptance criteria as a checklist with the exact commands that prove each one, known risks, and on re-plan an analysis of why the previous attempt fell short.
- **Re-plan triggers**: failed or partial executor report, status reviewer says not achieved, code reviewer advises changes, publisher reports a failure, a dependency appeared.

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

- **Job**: Runs when the executor claims completion. Independently verifies that the intended end state is achieved, using the planner's acceptance criteria. Re-runs the verifying commands itself. Trusts nothing from the executor report.
- **Read-only**: no edits. May run build/test/inspection commands.
- **Report**: for each acceptance criterion `MET` / `NOT MET` / `UNVERIFIABLE` with the evidence (command and output excerpt), plus an overall `ACHIEVED` | `NOT_ACHIEVED`, and a list of what is missing.
- The controller does not overrule the reviewer. Not achieved → re-plan (§3).

### 5.4 Code reviewer (sonnet)

- **Job**: Review the changes of this task (`git diff` against the task's starting commit, which the controller records in the prompt). Look for correctness bugs, missing tests, security issues, and breaks of the surrounding code's conventions.
- **Read-only**. Does not change code.
- **Report**: `ADVICE` = `ACCEPT` | `CHANGE`. Under `CHANGE` list only the changes that are worth it, each with file, reason and severity (`must` | `should`). **Good enough is fine**: no nitpicks, no style preferences, no speculative refactors. Only `must` items trigger another round automatically. `should` items are recorded in the progress file and do not block.

### 5.5 Publisher (haiku)

- **Job**: Commit and push the task's changes, wait for CI, report.
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
| Planner | opus | `default` | `Read Glob Grep Write(.plans/**)` | `Edit Bash` | 600 |
| Executor | haiku, sonnet or opus per brief | `acceptEdits` | `Read Glob Grep Edit Write Bash` | `Bash(git commit:*) Bash(git push:*) Bash(git tag:*) Bash(git reset:*) Bash(git rebase:*) Bash(git merge:*) Bash(gh:*)` | 1200 |
| Status reviewer | haiku | `default` | `Read Glob Grep Bash Write(.plans/**)` | `Edit Bash(git commit:*) Bash(git push:*)` | 600 |
| Code reviewer | sonnet | `default` | `Read Glob Grep Bash(git diff:*) Bash(git log:*) Bash(git show:*) Bash(git status:*) Write(.plans/**)` | `Edit` | 600 |
| Publisher | haiku | `acceptEdits` | `Read Glob Grep Bash(git:*) Bash(gh:*) Write(.plans/**)` | `Bash(git push --force:*) Bash(git push -f:*) Bash(git reset:*)` | 1800 |

Notes on the table:
- Tool-rule syntax (`Bash(git diff:*)`, `Write(.plans/**)`) depends on the CLI version. If a rule is not honored, verify with a trivial dry-run prompt before the real run, and tighten with instructions in the prompt as a second line of defense.
- The executor needs the build and test commands of the project in `Bash`. If the project needs a narrower allow-list, list the exact commands (for example `Bash(dotnet:*)`, `Bash(npm:*)`).
- Reviewers write only their report file. Their prompt names the exact path.

### Executor model escalation

The planner names the model in its brief. To escalate, the controller only changes `--model` (and the run attempt number). It does not decide this itself.

### After each run the controller

1. Checks the exit code (`0` normal, `124` timeout, other = CLI failure).
2. Reads `*.json` for `is_error`. On an API/CLI error (rate limit, auth, overload) retry the identical run once after a short wait. If it fails again, stop with a user-action stop (§8).
3. Checks that the report file exists. If missing, use the `result` field of the JSON as the report and note that in the progress file. If both are empty treat the run as failed.
4. Reads the report and decides the next step according to §3.

---

## 7. Report format

Every sub-agent writes its report to the given path in this shape, so the controller can parse it at a glance:

```markdown
# <role> report: <task id>, <attempt>

STATUS: COMPLETE | PARTIAL | BLOCKED | FAILED | NEEDS_USER | TIMEOUT
OUTCOME_ACHIEVED: yes | no | unknown

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
| **Finished** | Every task is complete and published | Summary per task, commits, open `should` review items |
| **User action needed** | A sub-agent returned `NEEDS_USER`, the planner returned `NEEDS_USER`, the working tree is unexpectedly dirty, credentials or CLI/API errors persist, or a decision outside the plan is required | The exact question or action, the task, where the reports are, how to resume |
| **Quality not reachable** | Attempt limits in §3 exceeded, the planner returns `ABORT`, or repeated failure loops persist even after escalation to opus | What was tried per attempt, the last errors, the planner's analysis, and a suggested manual next step |

Never continue past a stop by lowering the acceptance criteria. Never skip a failing task to do a later one, unless the planner explicitly reorders.

---

## 9. Progress tracking

The controller owns `.plans/<plan>-progress.md` and updates it after **every** sub-agent run, not only at task end. It survives context loss, so it must be enough to resume.

Per task keep:

- State: `not started` | `planning` | `executing (attempt n, model)` | `verifying` | `reviewing` | `publishing` | `done` | `blocked`
- Attempt counters (executor per model, review rounds, publish attempts)
- Start commit hash, final commit hash, CI result
- Open `should` review items
- Path to `runs/<ID>/` and a one-line note of the latest report's outcome

At the top: date, tasks done of total, current task, and the **next action** in one line (for example "run status reviewer, attempt 2, on MT-08").

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
