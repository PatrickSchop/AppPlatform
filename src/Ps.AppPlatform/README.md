# PS.AppPlatform

Reusable engine for small Azure Functions apps. Handles hosting, data layer, background task execution, static SPA content, and authentication â€” leaving only the app-specific endpoints and data model for you to write.

## Quick start

```csharp
// Program.cs
var builder = PlatformHostBuilder.Create(args, typeof(Program).Assembly);
builder.Services
    .AddPlatformData<AppDbContext>(builder.Configuration)
    .AddBackgroundTasks(builder.Configuration)
    .AddStaticContent(StaticContentSource.LocalFiles)
    .AddPlatformAuth(builder.Configuration);

var host = builder.Build();
var app = host.CreateServiceScope().ServiceProvider.GetRequiredService<AppDbContext>();
await app.Database.InitializeAsync();
host.Run();
```

## Configuration

The platform reads these sections from `appsettings.json`:

- **`database`**: SQL connection string. Uses managed identity by default via `AzureSqlTokenInterceptor`.
- **`backgroundTasks`**: Task execution settings (checkSchedule, maxConcurrentTasks, leaseSeconds).
- **`staticContent`**: Source (LocalFiles, Azure) and root path.
- **`authentication`** (optional): `{ "enabled": bool, "azureEntraId": { "tenantId": "...", "clientId": "..." } }`.
- **`webApp`** (optional): Public config served at `/configuration.json` (no secrets).

## Modules

- **Hosting**: `ServiceBuilder`, `PlatformConfiguration`, `AzureIdentityProvider`.
- **Data**: Generic `PlatformDbContext`, per-app derived contexts, embedded migration scripts.
- **Tasks**: Singleton `BackgroundTaskService<TContext>`, concurrency cap, orphan lease recovery.
- **Static**: SPA fallback, ETag caching, full MIME types, content negotiation.
- **Auth**: Default-deny, Functions-native `[Authorize]` attributes, role enforcement, CORS-first.
- **LLM** (optional): Text parsing via Azure OpenAI with automatic retry.

## Docs

See [the repository](https://github.com/PatrickSchop/AppPlatform) for detailed architecture and runbooks.

