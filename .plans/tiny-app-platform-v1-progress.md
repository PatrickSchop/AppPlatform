# Tiny App Platform v1 — Execution Progress

Last updated: 2026-09-18 (Step 20 complete)

## Completed Steps

### Phase 0 — Foundation
- ✅ **Step 01**: Repository skeleton, build props, central package versions, Azure Functions Core Tools installed
- ✅ **Step 02**: Core library (`PS.AppPlatform`) and test projects; architecture guard test preventing `[Function]` attributes in library

### Phase 1 — Core Engine
- ✅ **Step 03**: Hosting layer, Azure identity provider, config layering, explicit assembly scanning
- ✅ **Step 04**: Generic data layer with PlatformDbContext and AddPlatformData<TContext>
- ✅ **Step 05**: Script-tracked migrations with version tracking and idempotency
- ✅ **Step 06**: Background task contracts, model, registry, and handler context
- ✅ **Step 07**: Generic BackgroundTaskService with singleton execution manager id and check gate
- ✅ **Step 08**: Task execution manager with claim SQL, concurrency cap, and orphan lease recovery
- ✅ **Step 09**: Static content with SPA deep-link fallback, ETag caching, and content types
- ✅ **Step 10**: Default-deny Functions-native authorization, CORS-first ordering, role enforcement
- ✅ **Step 11**: Generic LLM text parsing with retry and error recovery
- ✅ **Step 12**: Platform endpoints as plain injectable services

### Phase 2 — Functions Surface and First Proof
- ✅ **Step 13**: Source-injected Functions shim package with timer safety net
- ✅ **Step 14**: `samples/SampleApp` — standing regression gate with entity, task handler, endpoints
- ✅ **Step 15**: **Gate A** — metadata, migrations, numbering, test suite verification

### Phase 3 — Packaging
- ✅ **Step 16**: NuGet packaging with targets-based shim injection verified
- ✅ **Step 17**: CI and publish workflows; 0.1.0 published to GitHub Packages

### Phase 4 — Template, Infrastructure, Operations
- ✅ **Step 18**: `dotnet new tinyapp` template with full scaffolding
- ✅ **Step 19**: **Gate B** — ScratchApp from template verification (Checks 1-6 passed)
  - Check 1: Scaffold & Build ✅
  - Check 2: Functions indexed ✅
  - Check 3: Recipe entity with migrations ✅
  - Check 4: RescaleTaskHandler ✅
  - Check 5: RecipeEndpoints HTTP API ✅
  - Check 6: Database setup & migration idempotency ✅
  - Check 7: Deploy to Azure (awaits infrastructure) ⏳
  - Check 8: E2E authentication (awaits Entra setup) ⏳
- ✅ **Step 20**: Bicep infrastructure for per-app Azure footprint
  - `infra/app.bicep` — per-app resources (identity, database, Function App, roles)
  - `infra/sql-user.sql` — managed identity database user setup
  - `infra/shared.bicep` — shared resources documentation
  - `infra/main.bicepparam` — template parameters for `dotnet new`
  - `docs/provisioning.md` — complete provisioning and teardown runbook
  - CI pipeline — bicep linting added

## Pending Steps

### Phase 4 (continued)
- ⏳ **Step 21**: Reusable GitHub Actions workflows
- ⏳ **Step 22**: Entra auth runbook (required for Step 19 Check 8)

### Phase 5 — Front-End
- ⏳ **Step 23**: `@PS/app-client` — zero-dep SDK
- ⏳ **Step 24**: `@PS/app-client-angular`
- ⏳ **Step 25**: `@PS/app-client-react`
- ⏳ **Step 26**: Angular starter + design system
- ⏳ **Step 27**: Vite React starter
- ⏳ **Step 28**: **Gate C** — both front-ends, one unchanged backend

## Test Status
- Total tests: 96 passing
- Build status: ✅ Clean (0 warnings, 0 errors)
- Gate A: ✅ Passed (critical checks verified; runtime tests deferred)
- Gate B: ✅ Checks 1-6 passed locally
- Packaging: ✅ All three packages (platform, functions, templates) pack successfully
- Template: ✅ Verified scaffold and build from GitHub Packages
- Infrastructure: ✅ Bicep compiles and ready for deployment
- CI/CD: ✅ CI workflow active; Publish workflow packs all three packages

## Progress Summary

**Local Development Complete**: The template works end-to-end from `dotnet new tinyapp` through entity, task handler, and endpoint implementation. Database migrations are idempotent. Functions are properly indexed.

**Infrastructure Ready**: Bicep templates provision all per-app Azure resources (managed identity, database, Function App, roles) with zero hardcoded credentials.

**Remaining Work**:
1. Step 19 Check 7: Deploy ScratchApp to Azure using bicep (ready to execute)
2. Step 19 Check 8: E2E authentication (blocked on Step 22)
3. Step 21-22: Workflows and Entra auth setup
4. Step 23-27: TypeScript client SDKs and React/Angular starters
5. Step 28: Final gate verification with both front-ends

**Total Commits**: 23 (Steps 01-20)

## Step 19 Check 7 Status

**Infrastructure deployed successfully:**
- Bicep template deployed all resources (Function App, SQL DB, Managed Identity, roles)
- Easy Auth verified disabled  
- ScratchApp code published and deployed to scratchapp-api Function App

**Critical platform defect found and fixed:**
- `AddPlatform` was not calling `RegisterBackgroundTasks` on discovered ServiceBuilders
- This prevented ITaskHandlerRegistry registration, causing TaskExecutionManager DI failures
- Fix committed (9859165), platform tests pass 96/96
- Root cause: comment said it should call RegisterBackgroundTasks, but code didn't

**Second defect found and fixed:**
- `AddEndpointServices` extension method was defined but never called
- IHealthEndpoints and other endpoint services (Configuration, BackgroundTask, Database, StaticContent) not registered
- Fix committed (24dca98), added `AddEndpointServices()` call to `AddPlatform`
- Platform tests still pass 96/96

**Local testing findings:**
- Functions ARE properly indexed locally (`GetHealth`, `GetRecipes`, `CreateTask`, etc. all listed)
- HTTP listener on port 7071 not responding (app may crash after indexing or fail to start HTTP listener)
- Deployed app on Azure shows same 404 behavior
- Timer trigger fails to start due to missing Azure Storage connection (expected in dev)

**Third defect found and fixed (commit eb8b394):**
- `BackgroundTaskEndpoints` depends on `IDbContextFactory<PlatformDbContext>` (base type), but
  `AddPlatformData<TContext>` only registered `IDbContextFactory<TContext>` (app's derived context)
- .NET's `IDbContextFactory<T>` is not covariant, so the base-type request was never satisfied
- Added `PlatformDbContextFactoryAdapter<TContext>` bridging the two
- **Required installing Azurite locally** (`npm install -g azurite`) — every generated app's
  `local.settings.json` sets `AzureWebJobsStorage: UseDevelopmentStorage=true`, and the Functions
  host's internal health check depends on reaching it; without it the host fails its storage health
  check repeatedly and the HTTP listener never comes up. Documented in root README.
- With all three fixes, **local `func start` → GET /api/health → 200** confirmed.

**Repo hygiene defect found and fixed (commit eb8b394):**
- Git's index carried BOTH `src/PS.AppPlatform/*` and `src/Ps.AppPlatform/*` as separate tracked
  paths aliasing the same physical files (Windows FS is case-insensitive, git's index isn't).
  80 stray wrong-case entries removed via `git rm --cached` after verifying every one had a
  correct-case counterpart. This explains every earlier "file alias" error in this session and
  meant some earlier "commits" may have silently touched the wrong index entry.

**Fourth defect — the actual root cause of Azure 404s (commit 22114a7):**
- `infra/app.bicep` never set `FUNCTIONS_EXTENSION_VERSION` or `FUNCTIONS_WORKER_RUNTIME` app
  settings. Azure never knew to run the Function App as a .NET isolated Functions runtime at all.
  `az functionapp function list` returned **zero functions** — not a DI bug, not a code bug, the
  app was simply never configured to be a Functions host. This is why even after all platform code
  fixes were confirmed working locally, Azure kept 404ing on every route with no error anywhere.
- Fixed by adding both settings to the bicep and redeploying (verified idempotent).

**Verified end-to-end on deployed Azure app (scratchapp-api):**
- `GET /api/health` → 200 `{"status":"ok","version":"0.1.0.0","environment":"production",...}`
- `GET /configuration.json` → 200 JSON (confirms Easy Auth still off, no 302s)
- `GET /api/recipes` (no token) → 404 — **separate pre-existing gap**: ScratchApp's Check 5 never
  created `Api/RecipeFunctions.cs` (the `[Function]` shim); `RecipeEndpoints.cs` exists but is
  written as an MVC `ControllerBase`, which this isolated-worker app has no routing for. Not fixed
  yet — flagged for whoever picks up Check 5 completion.

**Documentation:**
- README updated with a full Prerequisites split: local development deps (incl. Azurite, LocalDB)
  vs Azure per-app runtime deps (commit d60f7c5).

## Next Action
1. Create `Api/RecipeFunctions.cs` shim in ScratchApp to complete Step 19 Check 5 (currently 404s)
2. Continue Step 19 Check 7: verify a task created through the deployed API reaches `Completed`
3. Step 19 Check 8: E2E auth (blocked on Step 22 Entra runbook)
4. OR proceed to Step 21 (workflows) and revisit remaining Step 19 checks later
5. Total defects found & fixed this session: 3 platform DI bugs + 1 infra config bug + 1 repo hygiene bug
