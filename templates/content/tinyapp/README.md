# Tiny App

A minimal serverless application on PS.AppPlatform: database migrations, background tasks, and Entra authentication.

## Getting started

### Prerequisites

- .NET 10.0 SDK
- Azure Functions Core Tools (`func`)
- SQL Server (Azure SQL or localdb)
- Azure subscription (for deployment)

### 1. Create the database

Connect to your SQL Server and create an empty database named `TinyApp`.

```sql
CREATE DATABASE [TinyApp];
```

### 2. Run migrations

```powershell
dotnet run -- --migrate
```

This creates the schema versioning tables and any custom migration scripts in `Database/Scripts/`.

### 3. Start the Functions runtime

```powershell
func start
```

The app runs on `http://localhost:7071`.

- **`/api/configuration`** â€” public endpoint returning app config
- **`/health`** â€” liveness probe
- **`/static/**`** â€” SPA hosting (files from `wwwroot/`)
- All other `/api/**` routes â€” require Entra authentication

### 4. Add your first entity

Create a new file in the `Data/` folder:

```csharp
namespace TinyApp.Data;

public class YourEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
```

Add it to `AppDbContext`:

```csharp
public DbSet<YourEntity> YourEntities { get; set; } = null!;
```

Create a migration script in `Database/Scripts/101_CreateYourEntity.sql`:

```sql
IF NOT EXISTS (SELECT * FROM sys.objects
               WHERE object_id = OBJECT_ID(N'[dbo].[YourEntities]') AND type = N'U')
BEGIN
    CREATE TABLE [dbo].[YourEntities] (
        [Id]         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [Name]       NVARCHAR(200)    NOT NULL,
        [CreatedUtc] DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
END
```

Run migrations again:

```powershell
dotnet run -- --migrate
```

### 5. Add an endpoint

Create `Api/YourEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TinyApp.Data;
using PS.AppPlatform.Endpoints;

namespace TinyApp.Api;

public class YourEndpoints(AppDbContext db) : PlatformEndpoints
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List()
    {
        var items = await db.YourEntities.ToListAsync();
        return Ok(items);
    }
}
```

Register it in `AppServiceBuilder.cs`:

```csharp
public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    => services.AddScoped<YourEndpoints>();
```

Restart `func start` and test:

```bash
curl http://localhost:7071/api/your
```

### 6. Add a background task handler

Create `Tasks/YourTaskHandler.cs`:

```csharp
using TinyApp.Data;
using PS.AppPlatform.Tasks;

namespace TinyApp.Tasks;

public class YourTaskHandler : ITaskHandler<YourEntity>
{
    public async Task HandleAsync(YourEntity entity, TaskHandlerContext context, CancellationToken ct)
    {
        // Do work, report progress
        await context.UpdateProgressAsync(50);
        
        // More work...
        await context.UpdateProgressAsync(100);
    }
}
```

Register it in `AppServiceBuilder.cs`:

```csharp
public override void RegisterBackgroundTasks(IBackgroundTaskCollection tasks)
    => tasks.AddBackgroundTask<YourTaskHandler>("your-task");
```

### 7. Set up authentication

**Tenant and client IDs must be configured before deployment.**

See `docs/auth-setup.md` (in your PS.AppPlatform repository) for the Entra app registration and role setup runbook.

In `appsettings.json`, set:

```json
"authentication": {
  "tenantId": "your-tenant-id",
  "clientId": "your-app-id",
  "requiredRole": "your-app.user"
}
```

### 8. Deploy to Azure

Deployment uses bicep templates and GitHub Actions. See `infra/main.bicepparam` for infrastructure parameters, and `.github/workflows/deploy.yaml` for the workflow.

```powershell
git push
```

The workflow builds, tests, packages, and deploys the app.

## Project layout

```
TinyApp/
  Program.cs                    # Entry point, DI setup
  AppServiceBuilder.cs          # Custom service and task registration
  appsettings.json              # Configuration: auth, database, tasks
  host.json                     # Functions runtime settings
  local.settings.json           # Development secrets (git-ignored)
  
  Data/
    AppDbContext.cs             # EF Core context
    [YourEntity.cs]             # Your domain entities
  
  Api/
    [YourEndpoints.cs]          # HTTP endpoints (inherit PlatformEndpoints)
  
  Tasks/
    [YourTaskHandler.cs]        # Background task handlers
  
  Database/
    Scripts/
      100_InitialSchema.sql     # Your migrations (core: 000-099, app: 100+)
  
  .github/workflows/
    deploy.yaml                 # CI/CD pipeline
  
  infra/
    main.bicepparam             # Azure infrastructure parameters
```

## See also

- [PS.AppPlatform source](https://github.com/PatrickSchop/AppPlatform)
- [Background task lease renewal](docs/lease-renewal.md) (in platform repo)
- [Authentication setup](docs/auth-setup.md) (in platform repo)

