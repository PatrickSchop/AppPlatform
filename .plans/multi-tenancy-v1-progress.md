# Multi-tenancy v1 — Progress

**Updated:** 2026-09-30 · **MT-01–MT-08 done · next: MT-09**

Plan: [multi-tenancy-v1.md](multi-tenancy-v1.md) · Steps: [multi-tenancy-v1/](multi-tenancy-v1/)

## Status at a glance

| | Steps | State |
|---|---|---|
| Phase 0 — Prerequisites | MT-01 | ✅ complete |
| Phase 1 — Core tenancy | MT-02 – MT-07 | ✅ complete · **Gate D passed** |
| Phase 2 — Management app | MT-08 – MT-12 | ⏳ not started · **Gate E** outstanding |
| Phase 3 — Front-end and template | MT-13 – MT-16 | ⏳ not started · **Gate F** outstanding |

**8 of 16 steps complete.**

## Dependencies on the v1 plan

| This plan | Needs from v1 | v1 state (2026-09-29) |
|---|---|---|
| MT-01 – MT-10 | Phases 0–4 (platform `0.1.2`) | ✅ available |
| MT-11 (management UI) | Step 26 — Angular starter, `@PS/app-client-angular` | ⏳ not started |
| MT-13 onward | Step 28 — Gate C (clients and both starters) | ⏳ not started |

Phases 0–1 and MT-08 – MT-10 can run in parallel with v1 Phase 5. MT-11 waits for v1 Step 26.

## Steps

| Step | Outcome | State | Tests | Commit |
|---|---|---|---|---|
| MT-01 | `PlatformCommandLine` | ✅ | ✅ | ✅ |
| MT-02 | Tenancy contracts, config directory | ✅ | ✅ | ✅ |
| MT-03 | `TenantEntity`, filters, factories | ✅ | ✅ | ✅ |
| MT-04 | Tenant resolution, registry roles | ✅ | ✅ | ✅ |
| MT-05 | Tenant-aware background tasks | ✅ | ✅ | ✅ |
| MT-06 | `/api/me/tenants`, conditional shims | ✅ | ✅ | ✅ |
| MT-07 | **Gate D** — `MultiTenantSample` | ✅ | ✅ | ✅ |
| MT-08 | Management backend, bootstrap | ⏳ | | |
| MT-09 | Registry API, `--register` | ⏳ | | |
| MT-10 | Admin API, invitations | ⏳ | | |
| MT-11 | Management UI | ⏳ blocked on v1 Step 26 | | |
| MT-12 | **Gate E** — management deployed | ⏳ | | |
| MT-13 | `TenantSession` | ⏳ blocked on v1 Step 28 | | |
| MT-14 | Angular/React tenancy components | ⏳ | | |
| MT-15 | Template `--Tenancy`, deploy registration, `0.2.0` | ⏳ | | |
| MT-16 | **Gate F** — template app end to end | ⏳ | | |

## Gates

**Gate D (MT-07) — passed 2026-09-29.** All 18 checks passed against a real Functions host
(`func start`) with LocalDB and a real Entra token, using `ConfigTenantDirectory`. `gate-d.ps1`
(checks 3–9, 11–13, 15–16) passed twice in a row.

| # | Check | Result |
|---|---|---|
| 1 | `--migrate` twice | ✅ First applied `000`–`020` + `100`–`130` (7 scripts); second run "Database is up to date"; tenancy column check passed. Run via `dotnet exec MultiTenantSample.dll --migrate`, **not** `dotnet run -- --migrate` — see note below. |
| 2 | `functions.metadata` | ✅ `GetMyTenants` present with `"scriptFile": "MultiTenantSample.dll"` |
| 3 | `GET /api/me/tenants` no token | ✅ 401 |
| 4 | `GET /api/me/tenants` token, no header | ✅ 200, both tenants with correct roles |
| 5 | `GET /api/projects` token, no header | ✅ 409 `tenant_required` |
| 6 | `POST /api/projects` header C | ✅ 200 (after fixing a case-sensitive JSON deserialization bug in the sample, see below) |
| 7 | `POST /api/projects` header F | ✅ 403 (viewer) |
| 8 | `GET /api/projects` header C / F | ✅ `[C1]` / `[]` |
| 9 | `GET /api/projects` random-GUID header | ✅ 403 `tenant_forbidden` |
| 10 | SQL: `Projects.TenantId` | ✅ Stamped with Contoso's id by the save interceptor, code never set it |
| 11 | `GET /api/countries` header F | ✅ both seeded countries |
| 12 | Anonymous feedback via slug | ✅ visible to Fabrikam only |
| 13 | Reports, no `reporter` role | ✅ 403 |
| 14 | Reports, `reporter` added to Contoso | ✅ 200, counts for **both** tenants (confirmed by inserting a Fabrikam project directly via SQL, since a viewer can't create one) |
| 15 | `projects/count` task | ✅ Completes reliably, `statusMessage` reports Contoso's count only |
| 16 | Fabrikam sees Contoso's task | ✅ Not present in `GET /api/tasks` header F |
| 17 | Configured oid no longer matches | ✅ 403 `not_registered` |
| 18 | `SampleApp` regression | ✅ Auth, Notes CRUD and the `WordCount` background task all still work; see findings below |

### Findings while running Gate D through a real host

Exactly the v1 §5 lesson repeating: running MultiTenantSample against a real Functions host,
real SQL Server and a real token surfaced defects no construction-only or InMemory-provider
test had caught. All are fixed, all 173 unit/integration tests still pass, and both SampleApp
and MultiTenantSample were re-verified live afterward.

1. **`dotnet run -- --migrate` silently does the wrong thing** for an Azure Functions Worker
   SDK project: the SDK overrides `dotnet run`'s target to launch `func start` (passing
   `--migrate` through as a meaningless extra argument), starting the full Functions host
   instead of running the command-line path in `Program.cs`. The proven-correct invocation,
   already used by v1 Gate A, is `dotnet exec <dll> --migrate` from the build output
   directory. The MT-07 step doc's `dotnet run -- --migrate` snippet is wrong; step docs are
   historical records and were left as written, but future steps should use `dotnet exec`.
2. **`TaskExecutionManager`'s task-claim query crashed against real SQL Server.** MT-05 added
   a query filter to `BackgroundTask`. EF Core must compose a `WHERE` clause for that filter
   around any query against the entity, but the claim query is a multi-statement
   `DECLARE`/`IF`/`UPDATE...OUTPUT` batch, not a composable `SELECT` — `FromSqlInterpolated`
   threw `InvalidOperationException`. Invisible to `TaskExecutionManagerTests` because those
   run against EF's InMemory provider, which never performs this check. Fixed with
   `.IgnoreQueryFilters()` in `TaskExecutionManager.ClaimAndUpdateTasksAsync`
   (`src/PS.AppPlatform/Tasks/TaskExecutionManager.cs`) — correct regardless, since the claim
   query already runs on the unscoped factory and legitimately spans every tenant.
3. **Fire-and-forget task execution was fundamentally unreliable under the Functions .NET
   Isolated Worker local host**, for two stacked reasons, both pre-existing (not introduced by
   MT-05, just never exercised for real before): (a) `TaskExecutionManager` captured the
   per-invocation scoped `IServiceProvider` and used it to build each task's execution scope;
   that provider is disposed the instant the triggering HTTP/timer invocation returns, so a
   `Task.Run` picked up afterward could throw `ObjectDisposedException`. (b) even past that, a
   `Task.Run` continuation that outlives its triggering invocation raced against the Functions
   Worker's invocation-scoped logging pipeline and could stall indefinitely before its first
   `await`. Proved (by temporarily awaiting execution directly instead of detaching it) that
   the tenant-scoped execution logic itself was always correct — this was purely a dispatch
   reliability problem.

   **Redesigned per operator direction** (a "thorough fix," explicitly not a standing
   worker loop, since this app must scale to zero on a Consumption plan when idle):
   - `TaskExecutionManager.CheckAndStartTasksAsync` now runs a claimed batch with
     `await Task.WhenAll(...)` inside the invocation that claimed it, instead of detaching
     each task via `Task.Run`. Concurrency within the batch (bounded by
     `maxConcurrentTasks`) is unchanged — each task still gets its own DI scope via
     `IServiceScopeFactory` (captured instead of `IServiceProvider`, fixing (a) above too);
     what changed is that the caller now waits for the batch instead of returning first.
   - Removed `BackgroundTaskService.TriggerTaskCheck()` entirely — the self-HTTP-POST to
     its own `/api/tasks/check` that used to nudge execution after create/resume/status
     update. It was fire-and-forget with no credentials, so it would have 401'd against a
     tenancy-mode app the moment it worked at all. `backgroundTasks:apiBaseUrl` is gone
     from every `appsettings.development.json` (SampleApp, MultiTenantSample, the
     `tinyapp` template) and `IHttpClientFactory` is no longer a `BackgroundTaskService`
     dependency.
   - **Polling now drives execution.** `BackgroundTaskEndpoints.GetAllAsync`,
     `GetByIdAsync` and `GetNotificationsAsync` each run a claim-and-execute pass before
     answering. A client polling task status is what makes queued work progress — matching
     the operator's guidance that the design should partially depend on frontend polling.
     `POST /api/tasks/check` still exists for a caller that wants to nudge processing
     without reading state.
   - The timer (`ScheduledTaskCheck`, `backgroundTasks:checkSchedule`, still every 5
     minutes by default) is now explicitly documented as the safety net for tasks nobody
     is polling (`--migrate`-created tasks, an app that scaled to another instance) — not
     the primary path.
   - **Nothing here keeps the app "running" between invocations.** No standing loop, no
     `IHostedService`. Work only happens inside an invocation that was already executing
     (an HTTP call or a timer tick), so a Consumption-plan app still scales to zero once
     nobody is polling — the explicit operator constraint.
   - Verified: 3/3 consecutive attempts completed on the very first poll with SQL Server
     and a real token, both stand-alone and via `gate-d.ps1` run twice in a row.
     `docs/multi-tenancy.md` needed no changes (it never described the dispatch
     mechanism); `samples/SampleApp/docs/background-tasks.md` gained a section
     explaining how a task actually runs now.
4. **Own-code bug (not a platform defect):** `ProjectsEndpoints.CreateAsync` and
   `FeedbackEndpoints.SubmitAsync` used `JsonSerializer.Deserialize<T>(json)` with default
   options, which is case-sensitive — a `{"name": "C1"}` body from `curl` silently failed to
   bind to the `Name` record property. Fixed with `JsonSerializerOptions.Web`. The same latent
   bug exists in `samples/SampleApp/Api/NotesEndpoints.cs` (`Note` fields came back empty when
   posted with lowercase JSON) — confirmed during the check-18 regression pass, left
   unfixed since SampleApp is out of MT-07's scope; worth a small follow-up.

### Operator identity used for the run

Contoso `11111111-1111-1111-1111-111111111111` (editor), Fabrikam
`22222222-2222-2222-2222-222222222222` (viewer). Real oid/tid/API-client-id came from
`az ad signed-in-user show`, `az account show` and the `PS Apps API` app registration, and
live only in the gitignored `samples/MultiTenantSample/local.settings.json` — never
committed. `appsettings.development.json` (committed) has the tenant metadata only; the
`devDirectory:users` entry with the real oid is local-only, since this repo is public.

**Gate E (MT-12) — outstanding.** 12 checks; results go here.

**Gate F (MT-16) — outstanding.** 15 checks and the scaffold-to-deploy time; results go here.

## Decisions taken while planning

| Decision | Choice | Where |
|---|---|---|
| Membership lookup | Management API + per-app cache (5 min; 1 min for "not registered"; stale-if-error 1 h) | D1, MT-09 |
| Registration timing | At deployment (`--register`), not at startup | D2, MT-15 |
| Binding users to identities | One-time invite link; email is never matched | D5, MT-10 |
| Tenant resolution placement | A service inside `FunctionAuthorizationMiddleware`, not a separate middleware | D3, MT-04 |
| Token roles in registry apps | Dropped; the registry is the only source of roles | D3, MT-04 |
| Tenant query filter placement | EF model-finalizing convention, so every `TenantEntity` in the final model is filtered (fixes an MT-03 fail-open gap) | D4, MT-04 |
| `[TenantOptional]` with `requiredRole` | Tenant-less callers have no roles; such endpoints use `PlatformPolicies.AuthenticatedOnly` | D3, MT-04 → MT-06 |
| `TaskExecutionManager`'s own task bookkeeping reads | Read the claimed row directly via the unscoped factory instead of `IBackgroundTaskService.GetTaskStatusAsync`, since the manager's own scope has no request tenant and may run tasks for several tenants at once (the tenant-scoped read stays correct for `BackgroundTaskEndpoints`, which always has a resolved request tenant) | D4, MT-05 |
| Bootstrap admin ids | Repository variables, not secrets | D5, MT-12 |
| Background task dispatch | Polling-driven (task read endpoints claim-and-execute before answering) plus a timer safety net, not a fire-and-forget self-POST or a standing worker loop — must scale to zero on Consumption when idle | Gate D (MT-07) finding, `TaskExecutionManager`/`BackgroundTaskService` |

## Operator actions (when their steps arrive)

- MT-07: ✅ done — used `az ad signed-in-user show` / `az account show` instead of a decoded
  token; both give the same `oid`/`tid`.
- MT-12: create the `Management` resource group and deploy; set app settings; add the SPA
  redirect URI; set the `BOOTSTRAP_ADMIN_*` repository variables; grant the deploy principal
  `db_owner` on the `management` database; have a second Microsoft account ready.
- MT-16: create `PatrickSchop/TenantScratch`; add its origin to the SPA redirect URIs.

## Next

Start **MT-08** — Management backend: registry schema scripts, `LocalRegistryTenantDirectory`,
self-registration, `--bootstrap-admin`.

## Controller run log

- 2026-09-30 MT-08: first planner launch failed (nested-session guard); fixed by `env -u` prefix in the updated execution doc. Planner 1 returned SPLIT into MT-08a (scaffolding: projects, entities, RegistryDbContext, migration script, IPostMigrationStep), MT-08b (services, LocalRegistryTenantDirectory, self-registration, --bootstrap-admin), MT-08c (tests); executor model sonnet. Planner's Write(.plans/**) was denied, so its report was taken from the JSON `result` (summary only). Run dir `.plans/multi-tenancy-v1/runs/MT-08/`. Start commit 10cb25e. State: MT-08a executing (attempt 1, sonnet). Counters: executor sonnet 0/2, review rounds 0/2, publish 0/3.
- MT-08a: executor a1 (sonnet) COMPLETE, verifier ACHIEVED (174 tests), reviewer ACCEPT-with-should. Open should: EF index on Teams should be `HasIndex(t => t.TenantId)` to mirror SQL (RegistryDbContext.cs). State: publishing.
- MT-08a: DONE, commit 71f2666, CI passed. State: MT-08b executing (attempt 1, sonnet); start commit 71f2666.
- MT-08b: executor b1 (sonnet) COMPLETE, verifier ACHIEVED, reviewer CHANGE (1 must: UpsertApplicationAsync not in a single transaction; should: EnsureAdminAsync transaction, AddPlatformTenancy order). Re-planning (review round 1/2).
- MT-08b: b2 fixes COMPLETE, verifier ACHIEVED, reviewer ACCEPT (review round 2 done). Publishing.
- MT-08b: DONE, commit c22e2bc, CI passed. State: MT-08c executing (attempt 1, sonnet); start commit c22e2bc.
- MT-08c: executor c1 COMPLETE (193 tests). Running verify/review/publish via .plans/multi-tenancy-v1/runs/tail.sh (gated: verify ACHIEVED and review ACCEPT, else stops for re-plan).
- MT-08c: verifier ACHIEVED (193 tests), reviewer CHANGE (must: EnsureAdminAsync does not persist re-enable of a disabled user whose membership+assignment already exist; confirmed real by controller read of RegistryService.cs:173-184; should: add a test). Re-planning (review round 1/2).
- 2026-09-30 PAUSED by user during MT-08c executor attempt c2 (sonnet, 2/2 at sonnet). The run was killed mid-way: `runs/MT-08/18-exec-c2.*` has prompt/start only, no report. Working tree (uncommitted, on top of c22e2bc): tests/Management.Tests (5 test files, placeholder deleted) plus a modification of apps/Management/Registry/RegistryService.cs (the planner's SaveChangesAsync fix, unverified); the c2 regression test may or may not be present. Nothing of MT-08c is committed. One `dotnet` process (pid 1813) was still running after the stop; it may be an orphan of the killed run.
  Next action on resume: check `git diff`, then re-run the executor for MT-08c (c2 prompt is in runs/MT-08/18-exec-c2.prompt.md; tell it the tree is partially changed), then `runs/tail.sh MT-08 c2 19 c22e2bc ...` (verify, review, gated publish). After MT-08: MT-09, MT-10; MT-11 is blocked on v1 Step 26, MT-12 needs operator deploy, MT-13+ on v1 Step 28.
  WARNING: the killed executor left `RegistryService.cs` with the fix line commented out (`// await db.SaveChangesAsync(ct); // TEMPORARILY REMOVED FOR TEST PROOF`, after the re-enable block in EnsureAdminAsync). It must be restored before anything is committed.
- 2026-09-30 RESUMED under the updated execution doc (haiku first, read-only roles without Write, one review per task, headers only, cost lines). Tree as recorded: fix line still commented out; regression test present. MT-08c review round already used, so REVIEW: no; the fix is verified by the status reviewer. State: MT-08c executing (attempt c3, haiku, concrete brief from 17-plan3.report.md).
- cost 20-exec-c3.json claude-haiku-4-5-20251001 turns 19 usd 0.11
- cost 21-verify-c3.json claude-haiku-4-5-20251001 turns 23 usd 0.13
- cost 21-verify-c3.json claude-haiku-4-5-20251001 turns 23 usd 0.13
- cost 23-publish-c3.json claude-haiku-4-5-20251001 turns 21 usd 0.1
- MT-08: DONE (a=71f2666, b=c22e2bc, c=2a440f5; CI passed on all). Note: publisher also committed its own report as 2ce660c (runs/MT-08/23-publish-c3.report.md); runs/ files are otherwise untracked. Open should items: none. State: MT-09 planning; start commit 2ce660c. Run dir runs/MT-09/. Scripts: runs/tail.sh (verify [+review if REVIEW=yes] + gated publish; set PYTHONUTF8=1).
- cost MT-09/01-plan.json claude-opus-4-6,claude-haiku-4-5-20251001 turns 43 usd 1.61
- MT-09 planned: SPLIT into MT-09a (registry API + caller auth; REVIEW yes), MT-09b (caching + resolver), MT-09c (ManagementApiTenantDirectory + --register + tests); all haiku, briefs in runs/MT-09/01-plan.report.md (a: lines 21-138, b: 139-285, c: 286-end). Helper: runs/exec.sh. State: MT-09a executing (a1, haiku); start commit 2ce660c.
- cost MT-09/02-exec-a1.json claude-haiku-4-5-20251001 turns 94 usd 0.76
- MT-09a: executor a1 (haiku) COMPLETE (201 tests). Running verify + review + gated publish (tail.sh, REVIEW=yes).
- cost 03-verify-a1.json claude-haiku-4-5-20251001 turns 25 usd 0.15
- cost 04-review-a1.json claude-sonnet-4-6 turns 23 usd 0.73
- MT-09a: verifier ACHIEVED, reviewer CHANGE (1 must: RegistrationBody lacks ServicePrincipalId, PUT registers null principal so GET memberships always 403; see 04-review-a1.report.md). Re-planning (review round 1/1: no further code review, status reviewer verifies the fix).
- cost MT-09/05-plan2.json claude-opus-4-6 turns 27 usd 0.56
- MT-09a re-plan: EXECUTE haiku (a2), REVIEW no, brief = runs/MT-09/05-plan2.report.md (whole file). State: MT-09a executing (a2, haiku).
- cost MT-09/06-exec-a2.json claude-haiku-4-5-20251001 turns 30 usd 0.19
- MT-09a: a2 COMPLETE (202 tests). Running verify + gated publish (no 2nd review).
- cost 07-verify-a2.json claude-haiku-4-5-20251001 turns 27 usd 0.17
