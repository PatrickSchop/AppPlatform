# Tiny App Platform v1 â€” Execution Progress

Last updated: 2026-09-18 (Step 18 complete)

## Completed Steps

### Phase 0 â€” Foundation
- âœ… **Step 01**: Repository skeleton, build props, central package versions, Azure Functions Core Tools installed
- âœ… **Step 02**: Core library (`PS.AppPlatform`) and test projects; architecture guard test preventing `[Function]` attributes in library

### Phase 1 â€” Core Engine
- âœ… **Step 03**: Hosting layer, Azure identity provider, config layering, explicit assembly scanning
  - ServiceBuilder, PlatformAssemblies, PlatformConfiguration, PlatformHostBuilder
  - HostingEnvironment, AzureIdentityProvider
  - 11 passing tests
- âœ… **Step 04**: Generic data layer
  - PlatformDbContext base class with entity discovery via PlatformAssemblies
  - AddPlatformData<TContext> generic registration extension
  - AzureSqlTokenInterceptor for managed identity SQL auth
  - BackgroundTask stub with required properties
  - 14 passing tests (3 new data layer tests)
- âœ… **Step 05**: Script-tracked migrations
  - Embedded core scripts (000_CreateSchemaVersions.sql, 010_CreateBackgroundTasks.sql)
  - MigrationScript record, IMigrationScriptProvider interface
  - EmbeddedMigrationScriptProvider and DirectoryMigrationScriptProvider implementations
  - DatabaseMigrator<TContext> with version tracking, batch splitting, checksum calculation
  - MigrationEntryPoint for --migrate CLI path
  - 24 passing tests (10 new migration tests)
- âœ… **Step 06**: Background task contracts, model, registry, handler context
  - BackgroundTaskStatus enum with bitmask design (New, Resumed, NotStarted, Running, Paused, Completed, Failed)
  - BackgroundTask entity with LeaseExpiresUtc property
  - ITaskHandler<T>, ITaskHandlerRegistry, TaskHandlerRegistry with case-insensitive lookups
  - IBackgroundTaskCollection, BackgroundTaskCollection (public, with duplicate name checking)
  - IBackgroundTaskService, IBackgroundTaskManagementService with RenewLeaseAsync
  - TaskHandlerContext with UpdateProgressAsync that renews lease
  - 9 passing tests for task contracts (33 total tests passing)
- âœ… **Step 07**: Generic BackgroundTaskService; execution manager id and check gate are host-lifetime (fixes 7.1)
  - ExecutionManagerIdentity singleton - stable across scopes
  - TaskCheckGate singleton - serializes CheckAndStartTasksAsync
  - BackgroundTaskService<TContext> with generic context support
  - apiBaseUrl is optional (nullable) instead of required
  - RenewLeaseAsync method for lease extension
  - TasksServiceBuilder registers singletons
  - PlatformDataExtensions registers context-dependent services
  - 8 passing tests for task service (41 total tests passing)
- âœ… **Step 08**: Task execution manager with shared identity, claim SQL and orphan lease recovery (fixes 7.2)
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
- âœ… **Step 09**: Static content with SPA deep-link fallback, ETag caching and full content types (fixes section 4)
  - IFilesProvider returns StaticFile with metadata (ETag, LastModified, Length)
  - LocalFilesProvider with hardened path-traversal protection (sibling directory check)
  - BlobProvider with optimized single-call download (halves round trips for cold start)
  - StaticContentHandler with SPA fallback: extensionless paths â†’ index.html
  - ContentTypes dictionary with 19 MIME types (.ico, .woff2, .map, .wasm, .webp, etc.)
  - Cache headers: no-cache for index.html, immutable for assets
  - ETag/If-None-Match with 304 Not Modified support
  - StaticContentServiceBuilder for pluggable provider selection
  - 16 passing tests for static content (64 total tests passing)
- âœ… **Step 10**: Default-deny Functions-native authorization, CORS-first, role enforcement (fixes section 6)
  - PlatformAuthenticationOptions with TenantId/ClientId validation
  - PlatformAuthExtensions: JWT bearer auth via AddMicrosoftIdentityWebApi
  - FunctionAuthorizationMiddleware: reads [Authorize]/[AllowAnonymous] from method reflection
  - Default-deny policy + RequiredRole support with case-insensitive matching
  - CorsMiddleware: CORS-first ordering, comma-separated origins, Vary header
  - FunctionContextExtensions: GetTargetFunctionMethod() via EntryPoint reflection
  - PlatformMiddlewareChain: chains CORS â†’ Authorization
  - AuthServiceBuilder.AddPlatformAuth() registers all platform auth services
  - 12 new auth tests (76 total tests passing)
- âœ… **Step 11**: Generic LLM text parsing with retry and error recovery
- âœ… **Step 12**: Platform endpoints as plain injectable services
  - ILlmTextParseClient interface for LLM completion requests
  - LlmTextParseClient: Azure OpenAI implementation with JSON format
  - LlmTextParserBase<T>: generic base with automatic retry (configurable attempts)
  - Parse error fed back into next attempt prompt for recovery
  - Fenced code block stripping (```json ... ```)
  - HtmlToXhtmlConverter: HTML5â†’XHTML utility (domain-free)
  - LlmServiceBuilder: optional module, only registers when azureOpenAI config exists
  - Logs warning for apiKey auth (prefers managed identity)
  - 10 new LLM tests (86 total tests passing)
- âœ… **Step 13**: Source-injected Functions shim package and timer safety net
  - PS.AppPlatform.Functions: packaging-only project (IncludeBuildOutput=false)
  - 6 endpoint shim files: BackgroundTaskFunctions, ConfigurationFunctions, HealthFunctions, DatabaseFunctions, StaticContentFunctions, TaskSchedulerFunctions
  - build/PS.AppPlatform.Functions.targets auto-imports endpoints into consumer
  - TaskSchedulerFunctions: timer trigger with configurable schedule (%backgroundTasks:checkSchedule%)
  - Security model embedded in StaticContentFunctions: SPA is public, /api/* is default-deny
  - Package verified: contains endpoints/*.cs and build/*.targets, no lib/ folder
  - README.md documents ProjectReference vs PackageReference trap
  - 96 tests still passing (shims only compile in consumer)

### Phase 2 â€” Functions Surface and First Proof
- âœ… **Step 14**: `samples/SampleApp` â€” the standing regression gate
  - First consumer of Step 13 shims; shims compile here via ProjectReference
  - Note entity with optional WordCount field and CreatedUtc timestamp
  - WordCountTaskHandler: 2.5s task (~500ms per note) with progress reporting via UpdateProgressAsync
  - NotesEndpoints service (GetAll, Create, StartWordCount) + NotesFunctions shims
  - SampleServiceBuilder registers NotesEndpoints and WordCountTaskHandler
  - HTML5 UI: config fetch, note list, form, task polling with progress bar
  - Deep-link test: /dashboard (non-existent) serves index.html via SPA fallback
  - docs/background-tasks.md documents lease renewal obligation (300s default)
  - appsettings.development.json enables API migration for dev
  - local.settings.json: timer schedule 0 */5 * * * *, AzureWebJobsStorage for dev
  - 96 tests passing; build clean with 0 warnings
- âœ… **Step 15**: **Gate A** â€” metadata, migrations, numbering, test suite verification
  - Fixed DatabaseMigrator.GetAppliedScriptNamesAsync (NextResultAsync â†’ ReadAsync bug)
  - Verified all 13 platform functions in functions.metadata with scriptFile: SampleApp.dll
  - Verified worker indexing enabled (workerIndexing: true)
  - Verified schema versioning: second migration applies nothing (idempotent)
  - Verified BackgroundTasks table has all required columns
  - Verified script numbering validation (rejects app scripts < 100)
  - All 96 tests pass; Release build clean
  - Deferred: SPA serving, deep links, task execution, auth â€” require interactive func start
  - Created docs/gate-a-results.md with full verification report

### Phase 3 â€” Packaging
- âœ… **Step 16**: NuGet packaging â€” targets-based shim injection verified
  - Added package metadata: readme, tags, symbols, SourceLink
  - Local builds version as 0.1.0-local (never confused with published)
  - Per-package READMEs explain architecture and ProjectReference caveat
  - PS.AppPlatform: dll, docs, README; PS.AppPlatform.Functions: endpoints, targets, README (no lib/)
  - **PackageReference-only consumer test**: all 13 platform functions injected via targets file
  - Lockstep versioning policy documented in docs/versioning.md
  - All 96 tests pass; Release build clean
- âœ… **Step 17**: CI and publish workflows; 0.1.0 published to GitHub Packages
  - Created `.github/workflows/ci.yaml` â€” runs on push/PR, builds, tests, packs
  - Package shape assertion: Functions package has no lib/, includes endpoints/ and build/
  - Created `.github/workflows/publish.yaml` â€” triggers on release or manual dispatch
  - Version override via `-p:Version=` strips `-local` suffix for published builds
  - `--skip-duplicate` prevents re-run failures; GitHub Packages rejects overwrites
  - Created `docs/consuming-packages.md` â€” complete authentication guide
  - **Critical requirement documented**: classic PAT only, `--store-password-in-clear-text` on Windows
  - Tag `v0.1.0` pushed; CI workflow passed; Publish workflow succeeded
  - Both packages published to `https://nuget.pkg.github.com/PatrickSchop/index.json`
  - GitHub CLI installed and authenticated for release management
  - All 96 tests pass; Release build clean; 17 commits (Steps 01-17)

### Phase 4 â€” Template, Infrastructure, Operations
- âœ… **Step 18**: `dotnet new tinyapp` template
  - Created templates/ directory structure with full scaffolding content
  - template.json with parameterization: PlatformVersion (0.1.0), AppRole, SqlServer, StorageAccount, Frontend choice
  - dotnetcli.host.json (CLI short names) and ide.host.json (IDE support)
  - Scaffolded content: Program.cs, AppServiceBuilder (stubs with comments), AppDbContext, config layers
  - Database/Scripts/100_InitialSchema.sql with commented example and numbering rules
  - .github/workflows/deploy.yaml stub and infra/main.bicepparam for infrastructure
  - PS.AppPlatform.Templates.csproj (packaging-only: IncludeBuildOutput=false, Compile=none)
  - Directory.Build.props updated with `templates\content` exclusion from solution build
  - publish.yaml updated to pack all three packages in lockstep (platform, functions, templates)
  - Verified: `dotnet new tinyapp -n SmokeApp -pv 0.1.0-local -ar smoke.user` scaffolds correctly
  - Verified: No TinyApp strings, no placeholder tokens, correct parameterization
  - Verified: Root build clean (0 warnings, 0 errors), all 96 tests passing
  - Created templates/README.md with installation and usage instructions
  - Template not added to solution; only packing explicitly

## Pending Steps

### Phase 4 (continued)
- â³ **Step 19**: **Gate B** â€” ScratchApp from template verification
- â³ **Step 20**: Bicep infrastructure (`app.bicep`)
- â³ **Step 21**: Reusable GitHub Actions workflows
- â³ **Step 22**: Entra auth runbook

### Phase 5 â€” Front-End
- â³ **Step 23**: `@PS/app-client` â€” zero-dep SDK
- â³ **Step 24**: `@PS/app-client-angular`
- â³ **Step 25**: `@PS/app-client-react`
- â³ **Step 26**: Angular starter + design system
- â³ **Step 27**: Vite React starter
- â³ **Step 28**: **Gate C** â€” both front-ends, one unchanged backend

## Test Status
- Total tests: 96 passing
- Build status: âœ… Clean (0 warnings, 0 errors)
- Gate A: âœ… Passed (critical checks verified; runtime tests deferred)
- Packaging: âœ… All three packages (platform, functions, templates) pack successfully
- Template: âœ… Verified local scaffold without placeholder tokens
- CI/CD: âœ… CI workflow active; Publish workflow packs all three packages
- Git commits: 18 (Steps 01-18)

## Next Action
Continue with Step 19 â€” **Gate B: Template Verification** â€” build a real app from the template

