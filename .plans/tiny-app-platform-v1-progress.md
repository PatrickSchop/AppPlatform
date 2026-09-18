# Tiny App Platform v1 — Execution Progress

Last updated: 2026-09-18 (Step 12 complete)

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
- ✅ **Step 06**: Background task contracts, model, registry, handler context
  - BackgroundTaskStatus enum with bitmask design (New, Resumed, NotStarted, Running, Paused, Completed, Failed)
  - BackgroundTask entity with LeaseExpiresUtc property
  - ITaskHandler<T>, ITaskHandlerRegistry, TaskHandlerRegistry with case-insensitive lookups
  - IBackgroundTaskCollection, BackgroundTaskCollection (public, with duplicate name checking)
  - IBackgroundTaskService, IBackgroundTaskManagementService with RenewLeaseAsync
  - TaskHandlerContext with UpdateProgressAsync that renews lease
  - 9 passing tests for task contracts (33 total tests passing)
- ✅ **Step 07**: Generic BackgroundTaskService; execution manager id and check gate are host-lifetime (fixes 7.1)
  - ExecutionManagerIdentity singleton - stable across scopes
  - TaskCheckGate singleton - serializes CheckAndStartTasksAsync
  - BackgroundTaskService<TContext> with generic context support
  - apiBaseUrl is optional (nullable) instead of required
  - RenewLeaseAsync method for lease extension
  - TasksServiceBuilder registers singletons
  - PlatformDataExtensions registers context-dependent services
  - 8 passing tests for task service (41 total tests passing)
- ✅ **Step 08**: Task execution manager with shared identity, claim SQL and orphan lease recovery (fixes 7.2)
  - TaskExecutionManager<TContext> with shared ExecutionManagerIdentity and TaskCheckGate
  - Atomic claim SQL with concurrency cap and slot management
  - Resumed tasks prioritized before New tasks in claim ordering
  - LeaseExpiresUtc set when tasks are claimed
  - ReclaimExpiredLeasesAsync for orphan recovery
  - Fire-and-forget task execution with continuation-based error logging
  - Handler resolution from isolated async scope (fixes disposed-scope bug)
  - Fixed typos in error messages
  - Documentation on ITaskHandler<T> about lease/progress obligation
  - 7 passing tests for task execution (48 total tests passing)
- ✅ **Step 09**: Static content with SPA deep-link fallback, ETag caching and full content types (fixes section 4)
  - IFilesProvider returns StaticFile with metadata (ETag, LastModified, Length)
  - LocalFilesProvider with hardened path-traversal protection (sibling directory check)
  - BlobProvider with optimized single-call download (halves round trips for cold start)
  - StaticContentHandler with SPA fallback: extensionless paths → index.html
  - ContentTypes dictionary with 19 MIME types (.ico, .woff2, .map, .wasm, .webp, etc.)
  - Cache headers: no-cache for index.html, immutable for assets
  - ETag/If-None-Match with 304 Not Modified support
  - StaticContentServiceBuilder for pluggable provider selection
  - 16 passing tests for static content (64 total tests passing)
- ✅ **Step 10**: Default-deny Functions-native authorization, CORS-first, role enforcement (fixes section 6)
  - PlatformAuthenticationOptions with TenantId/ClientId validation
  - PlatformAuthExtensions: JWT bearer auth via AddMicrosoftIdentityWebApi
  - FunctionAuthorizationMiddleware: reads [Authorize]/[AllowAnonymous] from method reflection
  - Default-deny policy + RequiredRole support with case-insensitive matching
  - CorsMiddleware: CORS-first ordering, comma-separated origins, Vary header
  - FunctionContextExtensions: GetTargetFunctionMethod() via EntryPoint reflection
  - PlatformMiddlewareChain: chains CORS → Authorization
  - AuthServiceBuilder.AddPlatformAuth() registers all platform auth services
  - 12 new auth tests (76 total tests passing)
- ✅ **Step 11**: Generic LLM text parsing with retry and error recovery
- ✅ **Step 12**: Platform endpoints as plain injectable services
  - ILlmTextParseClient interface for LLM completion requests
  - LlmTextParseClient: Azure OpenAI implementation with JSON format
  - LlmTextParserBase<T>: generic base with automatic retry (configurable attempts)
  - Parse error fed back into next attempt prompt for recovery
  - Fenced code block stripping (```json ... ```)
  - HtmlToXhtmlConverter: HTML5→XHTML utility (domain-free)
  - LlmServiceBuilder: optional module, only registers when azureOpenAI config exists
  - Logs warning for apiKey auth (prefers managed identity)
  - 10 new LLM tests (86 total tests passing)

## Pending Steps

### Phase 1 — Core Engine (continued)
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
- Total tests: 96 passing
- Build status: ✅ Clean (0 warnings, 0 errors)
- Git commits: 12 (Steps 01-12)

## Next Action
Continue with Step 13 — Shim `.cs` files + auto-imported `.targets` + timer trigger
