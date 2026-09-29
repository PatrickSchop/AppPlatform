# Tiny App Platform v1 â€” Execution Plan

Container document. Derived from [source-project-analysis.md](source-project-analysis.md).

---

## 1. What is being built

A reusable "tiny app" platform â€” `PS.AppPlatform` â€” extracted from the
domain-free half of `C:\Dev\StockAnalysis`, so that a new side project can go from
nothing to a deployed, authenticated, background-task-capable app on a zero-idle-cost
Azure Functions consumption plan in under an hour.

Five deliverables:

| # | Deliverable | Form |
|---|---|---|
| 1 | Backend engine | `PS.AppPlatform` NuGet package |
| 2 | Functions endpoint surface | `PS.AppPlatform.Functions` NuGet package (source-injected `[Function]` shims) |
| 3 | Scaffolding | `dotnet new tinyapp` template package |
| 4 | Azure footprint | `infra/*.bicep` + reusable GitHub Actions workflows |
| 5 | Front-end | `@PS/app-client` + Angular/React adapters + two starters |

---

## 2. Scope decisions (locked)

These were decided before planning and constrain every step below.

1. **StockAnalysis is never modified.** `C:\Dev\StockAnalysis` is a **read-only
   reference**. Files are *ported* (read, cleaned, rewritten) into `C:\Dev\AppPlatform`,
   never moved. No step may write to the StockAnalysis tree.
2. **All four stages of Â§3 are in scope**, including the TypeScript clients and both
   front-end starters.
3. **Auth is implemented in code, configured by hand.** The default-deny authorization
   middleware, role enforcement and CORS ordering are built and tested here. Creating
   the Entra app registrations is a written runbook (Step 22) that the operator executes,
   because it needs tenant admin.
4. **Authorization is authentication-only, for any Microsoft account** (decided 2026-09-29).
   Work, school and personal accounts may all use these apps, so both registrations use the
   `AzureADandPersonalMicrosoftAccount` audience and `tenantId` is `common`, never a tenant
   GUID. `requiredRole` is empty by default.

   This is a trade, not a simplification: an App Role can only be assigned to a principal in
   your directory, so gating on one is incompatible with admitting arbitrary personal
   accounts. Roles remain fully supported for an app that sets `requiredRole`; they are just
   not the default. Every `/api/*` endpoint is still default-deny — authentication is the
   gate.

### 2.1 The consequence that shapes the plan

The analysis doc's Stage 1 gate was "StockAnalysis still runs". With decision (1) that
gate is gone, and deferring all validation to the `dotnet new` test at Step 19 would
mean building twelve steps of library code with nothing proving it works.

**So the plan introduces `samples/SampleApp` at Step 14** â€” a minimal but real consumer
inside the platform repo, with one entity, one task handler and one endpoint. From
Step 14 onward it is the regression gate: every later step must leave `SampleApp`
building, migrating and serving. It is also what keeps the source-injected shims
(Step 13) honest, since those `.cs` files are compiled nowhere else in the repo.

---

## 3. Conventions every step must follow

| Concern | Rule |
|---|---|
| Root namespace | `PS.AppPlatform` (+ `.Hosting`, `.Auth`, `.Data`, `.Tasks`, `.StaticContent`, `.Llm`, `.Endpoints`) |
| Visibility | **Everything the consumer or a shim touches is `public`.** The source is largely `internal`; that must not be carried over (Â§2.1 of the analysis). |
| TFM | Platform and tests: `net9.0`. Apps from `dotnet new tinyapp`: `net10.0`. Deliberate — see §3.2 |
| Language | `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` |
| Package versions | Central, in `Directory.Packages.props`. Never pin a version in a `.csproj`. |
| `[Function]` attributes | **Never** in `PS.AppPlatform` or any assembly a consumer references. Only in source-injected shims. See Â§4. |
| Secrets | Never in `appsettings.json`. Function App settings or Key Vault references only. |
| Per step | Ends green (`dotnet build` + `dotnet test`) and with one git commit. |

### 3.2 Why the platform targets `net9.0` while generated apps target `net10.0`

A library on `net9.0` is consumable from a `net10.0` app, so the split costs nothing at the
seam. Moving the platform up was tried on 2026-09-29 and reverted: on `net10.0` the
dependency graph resolves `System.Security.Cryptography.Xml` and
`Microsoft.AspNetCore.DataProtection` at `10.0.0`, which carry six high and one **critical**
advisory, and `10.0.1` carries the identical set — there is no patched version to move to.
With `TreatWarningsAsErrors`, NuGet audit turns those into 20 build errors.

The only ways forward were to suppress a critical advisory or to stop treating audit findings
as errors. Both defeat a safety check to buy tidiness, so the platform stays on `net9.0`,
which also keeps generated apps off that graph.

**Revisit when patched 10.0.x packages ship.** Until then this row is a deliberate decision,
not drift.

### 3.1 Toolchain prerequisites

Verified present on this machine: .NET SDK `10.0.101`, Node `v24.10.0`, git `2.43.0`.

~~**Missing: Azure Functions Core Tools (`func`).**~~ Installed in Step 01 — `func`
4.14.0 is present. Steps 15, 19 and 28 all use it.

**Also required locally:** Azurite (for `AzureWebJobsStorage` during `func start`) and
LocalDB. Without Azurite the host starts but the timer trigger's listener fails.

---

## 4. The two load-bearing technical constraints

Restated here because most steps depend on them.

### 4.1 Functions cannot see `[Function]` in a referenced assembly

Worker indexing is source-generator based, and a Roslyn generator only sees the current
compilation. Disabling worker indexing would restore package-hosted functions but costs
placeholder cold-start optimisation â€” unacceptable on a consumption plan.

**Therefore:** logic lives in `PS.AppPlatform` as plain injectable services
(`PS.AppPlatform/Endpoints/`); the ~10-line `[Function]` shims ship as **loose `.cs`
files** in the `PS.AppPlatform.Functions` package and are injected into the
consumer's compilation by an auto-imported `.targets` file. `PS.AppPlatform.Functions`
therefore produces **no lib assembly at all** â€” it is a content-and-targets package.

Verification is mechanical (Step 15): `functions.metadata` in the *consumer's* obj
folder must list the platform functions with `"scriptFile": "SampleApp.dll"`.

### 4.1a The security model

Stated once, here, because several steps depend on it and it is the kind of decision that
gets quietly reversed by someone trying to be careful:

> **The API is the security boundary.** Every `/api/*` endpoint is default-deny (Step 10); no
> data is reachable unauthorized. The SPA bundle is served to **anyone**, deliberately â€” there
> is nothing sensitive in HTML and JavaScript, and a browser's first navigation carries no
> bearer token anyway. A front-end that mishandles a 401 or 403 is ugly, not insecure.

`StaticContent`, `GetHealth` and `GetWebAppConfiguration` are therefore `[AllowAnonymous]`;
everything else is protected. Per §2(4) the gate is **authentication**, not a role: a caller
with no token gets 401 and a caller with a valid token gets through. A 403 appears only when
an app sets `requiredRole`.

The SPA gets the client id it signs in with from the **public** `webApp:auth` section of
`/configuration.json` — `tenantId` (`common`), `clientId` (the **SPA** registration, not the
API one) and `scopes`. Both ids are public by design; nothing secret is served. This differs from the source, where `Static.cs:25` carries the
repo's only `[Authorize]` â€” which, per Â§6, could not have been enforcing anything anyway.

Three consequences that are easy to miss:
- Anything under `webApp` in configuration is **public**, served unauthenticated at
  `/configuration.json`.
- Adding `[AllowAnonymous]` to a new endpoint is a security decision and deserves a review
  comment saying why.
- **App Service Easy Auth must stay off.** The platform authenticates in-process; Easy Auth
  intercepts ahead of it and turns `/api/*` 401s into 302 redirects, breaking both the SPA
  bootstrap and the CORS fix from Step 10. Step 20's bicep declares it disabled so a redeploy
  reverts any manual change; Steps 19 and 22 verify it. The plan assumes throughout that it
  is **not** enabled.

### 4.2 The DbContext must be per-app

`PlatformDbContext` in the library holds only `DbSet<BackgroundTask>` plus the
`Entity`-subclass conventions. Apps derive from it. Registration is generic â€”
`services.AddPlatformData<AppDbContext>(configuration)` â€” and every reflection scan
takes an explicit assembly list rather than `Assembly.GetExecutingAssembly()`.

---

## 5. Correctness fixes carried in (analysis Â§7, Â§4, Â§6, Â§3)

These are not optional extras; they are the reason to extract rather than copy. Each is
assigned to a step and verified there.

| Fix | Problem | Step |
|---|---|---|
| Schema version tracking | Scripts re-run every time; `004`/`005`/`006` have latent drift | 05 |
| Execution manager id | Scoped manager regenerates the id per request, so `maxConcurrentTasks` caps nothing | 07 |
| Orphan lease recovery | Tasks stuck `Running` under a dead worker are never reclaimed | 08 |
| Timer safety net | Only trigger is a fire-and-forget self-HTTP-POST | 13 |
| SPA deep links | `Static.cs` falls back to `index.html` only for the empty path â€” `/dashboard` 404s on refresh | 09 |
| Content types + caching | No `.ico`/`.woff2`/`.map`; no `ETag`/`Cache-Control` | 09 |
| Default-deny auth | One `[Authorize]` in the whole backend; the policy is never referenced | 10 |
| Endpoint metadata | Wrapped ASP.NET middleware cannot see `[Authorize]` on `[Function]` methods | 10 |
| CORS ordering | CORS runs after auth, so 401s arrive as opaque CORS errors | 10 |
| Scope leak | Both middleware resolve from the root provider | 10 |
| `tenantId == clientId` | Copy-paste error in `appsettings.json` | 22 |
| Workflow inputs | `dotnetversion` declared, `inputs.dotnet_version` read; default `9.0.x` vs `net10.0` | 21 |

**The four Step 10 rows above shipped inert and were repaired on 2026-09-28.** `UsePlatform()`
registered no middleware, so nothing in that row was enforced in any app. Four further defects
sat behind it: `AddPlatform` never called `AddPlatformAuth`; authorization was evaluated with
an empty policy name, which throws; `[AllowAnonymous]` was invisible because the shim lookup
matched static methods only, while every shim method is an instance method; and `CorsMiddleware`
wrote headers after the response had started. Gate A missed all of it because `SampleApp` had
no `authentication` section, which silently disables auth.

Two more shipped broken and were repaired on 2026-09-29, both on the deployment path:
`MigrationEntryPoint` never registered `IConfiguration`, so `--migrate` threw before reaching
the database whenever a managed identity was used; and `infra/app.bicep` emitted
`<server>..database.windows.net`, whose extra dot fails DNS and surfaces as a 500 after a
~63 second timeout.

**The lesson these share is worth more than the fixes.** Every one passed its step's tests,
because those tests asserted that a type resolves from DI or that an attribute exists — never
that a request through a running host produced the right status. Steps 23-28 must verify
behaviour end to end, not construction.

**Dropped rather than ported:** `TaskNotificationHub.cs`, `TaskNotificationHubEndpoint.cs`,
the `@microsoft/signalr` dependency, `WebApp/Program.cs` and the npm MSBuild targets.

**Not carried over at all:** the live Cognitive Services API key at
`App/appsettings.json:25`. It is in StockAnalysis git history and **must be rotated**
independently of this plan â€” see Step 22.

---

## 6. Target repository layout

```
C:\Dev\AppPlatform\
  AppPlatform.slnx
  Directory.Build.props            # TFM, nullable, warnings-as-errors
  Directory.Packages.props         # central package versions
  src/
    PS.AppPlatform/             # engine â€” no [Function] anywhere
      Hosting/  Auth/  Data/  Tasks/  StaticContent/  Llm/  Endpoints/
    PS.AppPlatform.Functions/   # content-only package
      build/PS.AppPlatform.Functions.targets
      endpoints/*.cs               # the [Function] shims
  samples/SampleApp/               # the regression gate (Step 14)
  templates/tinyapp/               # dotnet new template (Step 18)
  infra/                           # bicep (Step 20)
  clients/                         # @PS/app-client{,-angular,-react} (Steps 23-25)
  starters/angular/  starters/react/
  tests/PS.AppPlatform.Tests/
  docs/                            # runbooks
  .github/workflows/
```

---

## 7. Execution steps

Each step is a separate document under [tiny-app-platform-v1/](tiny-app-platform-v1/),
sized to be executed by a small model: explicit file paths, explicit signatures, a
copy-pasteable verification command and a stated expected result.

**Execute strictly in order.** Every step declares its dependency; nothing is parallel.

**Keep track of state.** Update `tiny-app-platform-v1-progress.md` after completing each step to document what was done, how many tests pass, and what step comes next. This allows re-entrant execution if the session ends.

**Commit and push after each step.** After each step completes green (build and tests pass):
1. `git add -A` to stage all changes
2. `git commit -m "Step NN: <description>"` with the commit message from the step document
3. `git push` to push to the remote repository â€” this is critical for preserving work
4. Update `tiny-app-platform-v1-progress.md` and commit/push that as well

Pushing is essential because it ensures work is not lost if the session ends or the local machine fails.

### Phase 0 â€” Foundation

| Step | Document | Outcome |
|---|---|---|
| 01 | [step-01-repo-skeleton.md](tiny-app-platform-v1/step-01-repo-skeleton.md) | Git repo, solution, build props, `func` installed |
| 02 | [step-02-core-project.md](tiny-app-platform-v1/step-02-core-project.md) | Core + test projects build green |

### Phase 1 â€” Core engine

| Step | Document | Outcome |
|---|---|---|
| 03 | [step-03-hosting-and-identity.md](tiny-app-platform-v1/step-03-hosting-and-identity.md) | `ServiceBuilder`, assembly registry, config layering, `AzureIdentityProvider` |
| 04 | [step-04-data-layer.md](tiny-app-platform-v1/step-04-data-layer.md) | `PlatformDbContext`, `AddPlatformData<TContext>`, SQL token interceptor |
| 05 | [step-05-migrations.md](tiny-app-platform-v1/step-05-migrations.md) | `DatabaseMigrator` + embedded core scripts + `__SchemaVersions` |
| 06 | [step-06-tasks-contracts.md](tiny-app-platform-v1/step-06-tasks-contracts.md) | Task model, enum, interfaces, registry, handler context |
| 07 | [step-07-tasks-service.md](tiny-app-platform-v1/step-07-tasks-service.md) | `BackgroundTaskService<TContext>`, singleton manager id |
| 08 | [step-08-tasks-execution-manager.md](tiny-app-platform-v1/step-08-tasks-execution-manager.md) | Claim SQL, concurrency cap, lease recovery |
| 09 | [step-09-static-content.md](tiny-app-platform-v1/step-09-static-content.md) | Providers, SPA fallback fix, content types, caching |
| 10 | [step-10-auth.md](tiny-app-platform-v1/step-10-auth.md) | Default-deny authorization, CORS-first, role enforcement |
| 11 | [step-11-llm.md](tiny-app-platform-v1/step-11-llm.md) | `LlmTextParserBase<T>`, `ILlmTextParseClient` |
| 12 | [step-12-endpoint-services.md](tiny-app-platform-v1/step-12-endpoint-services.md) | Endpoint logic as plain injectable services |

### Phase 2 â€” Functions surface and first proof

| Step | Document | Outcome |
|---|---|---|
| 13 | [step-13-functions-shim-package.md](tiny-app-platform-v1/step-13-functions-shim-package.md) | Shim `.cs` files + auto-imported `.targets` + timer trigger |
| 14 | [step-14-sample-app.md](tiny-app-platform-v1/step-14-sample-app.md) | `samples/SampleApp` â€” the standing regression gate |
| 15 | [step-15-stage1-verification.md](tiny-app-platform-v1/step-15-stage1-verification.md) | **Gate A:** metadata, migrate-twice, deep link, task lifecycle |

### Phase 3 â€” Packaging

| Step | Document | Outcome |
|---|---|---|
| 16 | [step-16-nuget-packaging.md](tiny-app-platform-v1/step-16-nuget-packaging.md) | Both packages pack; consumed from a local feed |
| 17 | [step-17-github-packages.md](tiny-app-platform-v1/step-17-github-packages.md) | Publish workflow + versioning policy |

### Phase 4 â€” Template, infrastructure, operations

| Step | Document | Outcome |
|---|---|---|
| 18 | [step-18-dotnet-new-template.md](tiny-app-platform-v1/step-18-dotnet-new-template.md) | `dotnet new tinyapp` |
| 19 | [step-19-template-verification.md](tiny-app-platform-v1/step-19-template-verification.md) | **Gate B:** ScratchApp from template, nothing copied |
| 20 | [step-20-bicep-infra.md](tiny-app-platform-v1/step-20-bicep-infra.md) | `app.bicep` â€” per-app Azure footprint |
| 21 | [step-21-reusable-workflows.md](tiny-app-platform-v1/step-21-reusable-workflows.md) | `build`/`deploy` as `workflow_call`, both bugs fixed |
| 22 | [step-22-entra-auth-runbook.md](tiny-app-platform-v1/step-22-entra-auth-runbook.md) | Shared app registration + per-app App Role runbook |

### Phase 5 â€” Front-end

| Step | Document | Outcome |
|---|---|---|
| 23 | [step-23-ts-client-core.md](tiny-app-platform-v1/step-23-ts-client-core.md) | `@PS/app-client` â€” zero-dep SDK |
| 24 | [step-24-ts-client-angular.md](tiny-app-platform-v1/step-24-ts-client-angular.md) | `@PS/app-client-angular` |
| 25 | [step-25-ts-client-react.md](tiny-app-platform-v1/step-25-ts-client-react.md) | `@PS/app-client-react` |
| 26 | [step-26-angular-starter.md](tiny-app-platform-v1/step-26-angular-starter.md) | Angular starter + design system |
| 27 | [step-27-react-starter.md](tiny-app-platform-v1/step-27-react-starter.md) | Vite React starter |
| 28 | [step-28-final-verification.md](tiny-app-platform-v1/step-28-final-verification.md) | **Gate C:** both front-ends, one unchanged backend |

---

## 8. The three gates

Progress is only real at a gate. If a gate fails, fix forward â€” do not start the next
phase.

**Gate A (Step 15) â€” the seam holds.**
`SampleApp/obj/.../functions.metadata` lists `GetNotificationTasks`, `GetTasks`,
`CheckTasks`, `StaticContent`, `GetWebAppConfiguration` with `"scriptFile": "SampleApp.dll"`;
`--migrate` run twice reports zero applied on the second run; a hard refresh of
`/dashboard` serves `index.html`; a task created through the API reaches `Completed`.

**Gate B (Step 19) â€” the exercise pays off.**
`dotnet new tinyapp -n ScratchApp`, then add one entity, one `ITaskHandler<T>` and one
endpoint, and get a working app with **zero files copied from StockAnalysis or SampleApp**.
Target: under an hour from `dotnet new` to deployed.

Auth expectation, per §2(4): **401 without a token, 401 with a malformed one, 200 with a
valid one.** There is no 403 to observe unless `requiredRole` is set.

**Gate C (Step 28) â€” front-end freedom is real.**
The React starter and the Angular starter both run background-task progress against the
**same unmodified backend build**.

Gate C needs a real sign-in, because the Notes routes it exercises are protected. `SampleApp`
ships placeholder ids so it demonstrates default-deny without carrying a tenant identity in
the repository, so supply real ids at run time through environment variables —
`authentication__azureEntraId__clientId` (the API registration) and `webApp__auth__clientId`
(the SPA registration) — or run Gate C against the deployed ScratchApp.

---

## 9. Deliberately out of scope for v1

- **Migrating StockAnalysis** onto the platform. Locked out by decision Â§2(1); it is a
  follow-on plan once the packages are published.
- **Multi-tenancy.** No `UserId`/`TenantId`, no query filters. The per-app-database model
  sidesteps it. If an app later needs per-user data, the platform grows a
  `UserScopedEntity` convention â€” it does not get invented per app.
- **Schema parameterisation** of the claim SQL. `[dbo].[BackgroundTasks]` stays hard-coded;
  it only matters if apps ever share one database, which this model rejects.
- **Cleaning the ~35 AI-generated status docs** at the StockAnalysis repo root â€” that tree
  is read-only here.
- **Rotating the leaked Cognitive Services key.** Flagged in Step 22, but the action is the
  operator's and happens in the StockAnalysis repo and the Azure portal.
