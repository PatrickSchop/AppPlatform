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

## Next Action
Continue with Step 19 Check 7 — deploy ScratchApp to Azure using `infra/app.bicep`
