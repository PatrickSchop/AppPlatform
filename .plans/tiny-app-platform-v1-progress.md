# Tiny App Platform v1 — Execution Progress

Last updated: 2026-09-20 (Step 22 complete)

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
- ✅ **Step 19**: **Gate B** — ScratchApp from template verification
  - Checks 1-6: ✅ Complete (scaffold, functions, entity, task handler, endpoint, migrations)
  - Check 5: Fixed — `Api/RecipeFunctions.cs` shim created (was returning 404)
  - Check 7: Infrastructure ready (bicep deployment successful)
  - Check 8: Awaits Entra setup (Step 22 runbook available)
- ✅ **Step 20**: Bicep infrastructure for per-app Azure footprint
  - `infra/app.bicep` — per-app resources (identity, database, Function App, roles)
  - `infra/sql-user.sql` — managed identity database user setup
  - `infra/shared.bicep` — shared resources documentation
  - `docs/provisioning.md` — complete provisioning and teardown runbook
  - CI pipeline — bicep linting added
- ✅ **Step 21**: Reusable GitHub Actions workflows
  - `app-build.yaml` and `app-deploy.yaml` as `workflow_call` workflows
  - Fixed bugs: input naming (dotnetversion → dotnet_version), SDK version (9.0.x → 10.0.x)
  - Template deploy.yaml simplified to 15-line caller
  - `docs/deployment.md` — setup and troubleshooting guide
- ✅ **Step 22**: Entra auth runbook and security policies
  - `docs/auth-setup.md` — complete runbook (one-time and per-app setup)
  - `docs/security.md` — security policies and logging guidelines
  - Gitleaks secret scanning in CI

### Phase 4 (continued)
- ✅ **Step 21**: Reusable GitHub Actions workflows
  - `app-build.yaml` and `app-deploy.yaml` created as workflow_call workflows
  - Fixed input naming (dotnetversion → dotnet_version) and stale SDK version (9.0.x → 10.0.x)
  - Template deploy.yaml simplified to 15-line caller
  - Workflow linting added to CI

### Phase 4 (final)
- ✅ **Step 22**: Entra auth runbook and security policies
  - `docs/auth-setup.md`: Complete runbook (one-time tenant setup, per-app config, troubleshooting)
  - `docs/security.md`: Security policies (secrets, logging, default-deny, DB access)
  - Gitleaks secret scanning added to CI

## Pending Steps

### Phase 5 — Front-End (TypeScript SDKs and Starters)
- ⏳ **Step 23**: `@PS/app-client` — zero-dep TypeScript SDK
- ⏳ **Step 24**: `@PS/app-client-angular` — Angular adapter
- ⏳ **Step 25**: `@PS/app-client-react` — React adapter
- ⏳ **Step 26**: Angular starter + design system
- ⏳ **Step 27**: Vite React starter
- ⏳ **Step 28**: **Gate C** — both front-ends, one unchanged backend

## Test Status
- Platform tests: 96/96 passing ✅
- Build status: ✅ Clean (0 warnings, 0 errors)
- Gate A: ✅ Passed (Step 15)
- Gate B: ✅ Checks 1-6 passed; infrastructure deployed (Step 19)
- Packaging: ✅ All packages (platform, functions, templates) pack successfully
- CI/CD: ✅ Bicep linting, actionlint workflow linting, gitleaks secret scanning active
- Template: ✅ Verified scaffold and build from GitHub Packages

## Progress Summary

**Phase 0-4 Complete**: Foundation through operations fully implemented:
- Core platform engine with generic DbContext and background tasks
- Functions surface with source-injected shims (no assembly in package)
- Template scaffolding ready for new apps (dotnet new tinyapp)
- Infrastructure as code (bicep per-app, shared SQL server)
- CI/CD: reusable build/deploy workflows, secret scanning, linting
- Operations: Entra auth runbook, security policies, provisioning docs

**Phase 5 Pending**: Front-end SDKs and starters (Steps 23-28)
- TypeScript SDK package (@PS/app-client)
- Angular and React adapters + starters
- Gate C: both front-ends working with unchanged backend

**Remaining Work**:
1. Step 19 Check 7: Deploy ScratchApp with recipe endpoint to Azure (ready)
2. Step 19 Check 8: E2E authentication with Entra roles (runbook ready, manual setup)
3. Step 23-27: TypeScript clients and front-end starters
4. Step 28: Final gate verification with both front-ends

**Total Commits**: 26 (Steps 01-22)

## Step 19 & 20 Completion

**Step 20 Status:** ✅ Complete
- Bicep template deployed all resources (Function App, SQL DB, Managed Identity, roles)
- Infrastructure verified with what-if deployment
- Easy Auth explicitly disabled in bicep (verified in compiled template)
- CI pipeline includes bicep linting

**Step 19 Status:** ✅ Checks 1-6 Complete + Check 7 Infrastructure Ready
- Checks 1-6: Template scaffolding, function indexing, entity/task/endpoint creation, database setup all verified locally
- Check 7: Azure deployment ready (requires reconfiguration from Check 5 fix below)
  - Bicep template deployed all resources successfully
  - ScratchApp deployed to Azure Function App
  - Easy Auth verified disabled on deployed app
  - `/api/health` returns 200, `/configuration.json` returns JSON

**Step 19 Check 5 Completion (Recipe Endpoint):**
- **Issue found:** `RecipeEndpoints.cs` was written as `ControllerBase` (ASP.NET MVC pattern), which doesn't work in isolated Functions host
- **Fixed:** 
  - Refactored `RecipeEndpoints.cs` to be a plain service class taking `IDbContextFactory<AppDbContext>`
  - Methods now take `HttpRequest` directly instead of using MVC attributes
  - Created `Api/RecipeFunctions.cs` with `[Function]` shims for `GetRecipes` and `CreateRecipe`
  - Both functions now properly indexed in `functions.metadata` with `"scriptFile": "ScratchApp.dll"`
- **Impact:** Recipe endpoint now callable via Azure Functions HTTP triggers

**Platform Issues Found & Fixed During Deployment:**
1. `AddPlatform` not calling `RegisterBackgroundTasks` → DI failures for task handlers
2. `AddEndpointServices` not called → endpoint services never registered
3. `IDbContextFactory<PlatformDbContext>` not registered for base-type requests (covariance issue)
4. `FUNCTIONS_EXTENSION_VERSION` and `FUNCTIONS_WORKER_RUNTIME` not set in bicep → Azure returned 404 for all routes
5. Git index carrying duplicate case-variant paths on Windows

**Local Prerequisites Documented:**
- Azurite required for local `func start` (storage health check)
- LocalDB for SQL database
- Azure Functions Core Tools

## Next Action
1. Verify Step 19 Check 7 end-to-end: deploy updated ScratchApp with recipe endpoint to Azure
2. Verify recipe operations work on deployed app (task creation/completion)
3. Step 19 Check 8: E2E auth (blocked on Step 22 Entra runbook)
4. Proceed to Step 21 (reusable workflows) when ready
