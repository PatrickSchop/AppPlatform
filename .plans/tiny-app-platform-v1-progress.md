# Tiny App Platform v1 — Execution Progress

Last updated: 2026-09-18

## Completed Steps

### Phase 0 — Foundation
- ✅ **Step 01**: Repository skeleton, build props, central package versions, Azure Functions Core Tools installed
- ✅ **Step 02**: Core library (`Wisdi.AppPlatform`) and test projects; architecture guard test preventing `[Function]` attributes in library

### Phase 1 — Core Engine
- ✅ **Step 03**: Hosting layer, Azure identity provider, config layering, explicit assembly scanning
  - ServiceBuilder, PlatformAssemblies, PlatformConfiguration, PlatformHostBuilder
  - HostingEnvironment, AzureIdentityProvider
  - 11 passing tests
- ✅ **Step 04**: Generic data layer
  - PlatformDbContext base class with entity discovery via PlatformAssemblies
  - AddPlatformData<TContext> generic registration extension
  - AzureSqlTokenInterceptor for managed identity SQL auth
  - BackgroundTask stub with required properties
  - 14 passing tests (3 new data layer tests)
- ✅ **Step 05**: Script-tracked migrations
  - Embedded core scripts (000_CreateSchemaVersions.sql, 010_CreateBackgroundTasks.sql)
  - MigrationScript record, IMigrationScriptProvider interface
  - EmbeddedMigrationScriptProvider and DirectoryMigrationScriptProvider implementations
  - DatabaseMigrator<TContext> with version tracking, batch splitting, checksum calculation
  - MigrationEntryPoint for --migrate CLI path
  - 24 passing tests (10 new migration tests)

## Pending Steps

### Phase 1 — Core Engine (continued)
- ⏳ **Step 06**: Background task model, enum, interfaces, registry, handler context
- ⏳ **Step 07**: `BackgroundTaskService<TContext>`, singleton manager id
- ⏳ **Step 08**: Claim SQL, concurrency cap, lease recovery
- ⏳ **Step 09**: Static content providers, SPA fallback fix, content types, caching
- ⏳ **Step 10**: Default-deny authorization, CORS-first, role enforcement
- ⏳ **Step 11**: `LlmTextParserBase<T>`, `ILlmTextParseClient`
- ⏳ **Step 12**: Endpoint logic as plain injectable services

### Phase 2 — Functions Surface and First Proof
- ⏳ **Step 13**: Shim `.cs` files + auto-imported `.targets` + timer trigger
- ⏳ **Step 14**: `samples/SampleApp` — the standing regression gate
- ⏳ **Step 15**: **Gate A** — metadata, migrate-twice, deep link, task lifecycle

### Phase 3 — Packaging
- ⏳ **Step 16**: NuGet packaging for both packages
- ⏳ **Step 17**: GitHub Actions publish workflow + versioning policy

### Phase 4 — Template, Infrastructure, Operations
- ⏳ **Step 18**: `dotnet new tinyapp` template
- ⏳ **Step 19**: **Gate B** — ScratchApp from template verification
- ⏳ **Step 20**: Bicep infrastructure (`app.bicep`)
- ⏳ **Step 21**: Reusable GitHub Actions workflows
- ⏳ **Step 22**: Entra auth runbook

### Phase 5 — Front-End
- ⏳ **Step 23**: `@wisdi/app-client` — zero-dep SDK
- ⏳ **Step 24**: `@wisdi/app-client-angular`
- ⏳ **Step 25**: `@wisdi/app-client-react`
- ⏳ **Step 26**: Angular starter + design system
- ⏳ **Step 27**: Vite React starter
- ⏳ **Step 28**: **Gate C** — both front-ends, one unchanged backend

## Test Status
- Total tests: 24 passing
- Build status: ✅ Clean (0 warnings, 0 errors)
- Git commits: 5 (Steps 01-05)

## Next Action
Continue with Step 06 — Background task model, enum, interfaces, registry, handler context
