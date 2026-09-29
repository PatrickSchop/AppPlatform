# Tiny App Platform v1 — Progress

**Updated:** 2026-09-29 · **Phase 0-4 complete · no blockers open · Phase 5 ready to start**

## Status at a glance

| | Steps | State |
|---|---|---|
| Phase 0 — Foundation | 01-02 | ✅ complete |
| Phase 1 — Core engine | 03-12 | ✅ complete |
| Phase 2 — Functions surface | 13-15 | ✅ complete · **Gate A** passed |
| Phase 3 — Packaging | 16-17 | ✅ complete |
| Phase 4 — Template, infra, ops | 18-22 | ✅ complete · **Gate B** deploy/auth passed, one item to redo |
| Phase 5 — Front-end | 23-28 | ⏳ not started · **Gate C** outstanding |

**22 of 28 steps complete.** No blockers are open; Phase 5 may begin at Step 23. One Gate B
checklist item should be redone on the current ScratchApp — see Gates.

| Signal | State |
|---|---|
| Tests | 98 passing, 0 failing (Windows and Linux CI) |
| Build | clean, 0 warnings, `TreatWarningsAsErrors` on |
| CI | green end to end, every step executing |
| Published packages | `0.1.1` on GitHub Packages |
| Local packages | `0.1.2-local` in `nupkg/` (unreleased fixes) |

## Gates

**Gate A (Step 15) — passed.** Shims indexed into the consumer assembly; migrations
idempotent; SPA deep links served; task lifecycle completes.

> Gate A did not cover authorization, because `SampleApp` carried no `authentication`
> section and auth silently disables itself without one. `SampleApp` now enables it, so the
> gate covers default-deny from here on.

**Gate B (Step 19) — deploy and auth half passed 2026-09-29**, verified against the deployed
ScratchApp:

| Check | Result |
|---|---|
| Scaffolds and builds from the template | ✅ |
| Platform functions indexed as `ScratchApp.dll` via targets injection | ✅ |
| Migrations apply, and reapply clean on a second run | ✅ |
| Deployed to Azure and serving | ✅ |
| Easy Auth confirmed off | ✅ |
| Anonymous routes → 200 | ✅ |
| Protected route, no token → 401 | ✅ |
| Protected route, malformed token → 401 (not 500) | ✅ |
| Protected route, valid token → 200 | ✅ |

The token used was issued to a **personal Microsoft account**, which is the model in §2(4).

**One checklist item is not demonstrated on the current app.** Gate B also asks for one
entity, one `ITaskHandler<T>` and one endpoint added with nothing copied. That was done on an
earlier ScratchApp iteration, but this one was regenerated to pick up the platform fixes and
carries only the scaffold, so the claim rests on a build that no longer exists. Redo it on the
current app before calling Gate B closed in full — it is the half that proves the *template*,
as opposed to the deployment path proven above.

*Restore came from the local feed rather than GitHub Packages — see Operator actions.*

**Gate C (Step 28) — outstanding.** Needs both starters driving background-task progress
against one unchanged backend build, with a real sign-in.

## Phase 5 readiness

The 23→24→25→26→27→28 chain is strictly linear, each step depending only on its
predecessor. Everything it assumed but did not have now exists:

- `/configuration.json` serves `auth: { tenantId, clientId, scopes }` from `webApp:auth`, in
  both the template and `SampleApp`.
- The platform enforces authorization, so protected routes behave as the starters expect.
- The TFM split is a recorded decision (§3.2), not drift.

## Environment

| | |
|---|---|
| Platform repo | `PatrickSchop/AppPlatform` (**private**) |
| Verification app | `PatrickSchop/ScratchApp` → `https://scratchapp-api.azurewebsites.net` |
| Azure | resource group `ScratchApp` (westeurope); database `scratchapp` on `pschop-db` in `ApplicationsShared` |
| Entra | `PS Apps API` `c5692707-…` and `PS Apps SPA` `28267d47-…`, both `AzureADandPersonalMicrosoftAccount` |
| Tenant | `6f033fd3-…` · apps authenticate through the `common` authority |

The Azure CLI is pre-authorized on the API registration, so `az account get-access-token
--resource api://<api-client-id>` yields a token for testing protected routes without an
interactive consent prompt.

## Operator actions outstanding

These need a human; none blocks Phase 5.

1. **A PAT for package restore.** `AppPlatform` is private, so consumers cannot restore its
   packages with a workflow's own `GITHUB_TOKEN` — it fails with 403. Each consuming
   repository needs a classic PAT with `read:packages` as a secret. Gate B was therefore
   verified against the local feed; the published-package path is proven by CI publishing
   `0.1.1` successfully, but not by a consumer restoring it. See `docs/consuming-packages.md`.
2. **Publish the fixed platform.** `0.1.2-local` carries the migration and bicep fixes and is
   not yet released. Publish it before another app is generated.
3. **Rotate the leaked Cognitive Services key** in the StockAnalysis repo (carried from
   Step 22; out of scope here but still unresolved).

## Defect history

Nine defects found in supposedly complete steps. They shared one cause: every step's tests
asserted that a type resolves from DI or that an attribute exists, never that a request
through a running host produced the right status. Recorded in §5 of the plan.

**Authorization never ran (2026-09-28).** Five compounding defects meant default-deny was
inert in every app: `UsePlatform()` registered no middleware; `AddPlatform` never called
`AddPlatformAuth`; authorization used an empty policy name, which throws; `[AllowAnonymous]`
was invisible because the shim lookup matched static methods only; and `CorsMiddleware` wrote
headers after the response had started.

**Migrations never ran against a deployed database (2026-09-29).** `MigrationEntryPoint` never
registered `IConfiguration`, so `--migrate` threw before reaching the database whenever a
managed identity was used; `infra/app.bicep` emitted `<server>..database.windows.net`, whose
extra dot fails DNS and surfaces as a 500 after ~63 seconds.

**CI was not running what it claimed (2026-09-28/29).** `actionlint` exited 127 on every run
since Step 21, and because it failed, the secret scan and packaging assertions after it were
skipped every time. `.gitleaks.toml` had a schema error and omitted `[extend] useDefault`, so
the scan would have run with no rules. The test suite was tracked twice under case-variant
paths, and only one has the csproj CI builds.

**The deploy workflow could not have worked.** It ran `dotnet App.dll --migrate` with a
hard-coded assembly name; read three `AZURE_*` secrets it never declared; gated OIDC on
repository visibility; and tested an `env` value that was never mapped. The template's caller
passed `TINYAPP-NAME`, which is not a template symbol.

Each fix carries a regression test that fails without it.

## Next

Start **Step 23** — `@PS/app-client`, the zero-dependency TypeScript SDK.
