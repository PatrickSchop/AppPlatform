# Tiny App Platform

A reusable platform for building small Azure Functions-based applications with integrated authentication, background tasks, and a TypeScript client SDK.

Extracted from the domain-free half of [StockAnalysis](https://github.com/PatrickSchop/StockAnalysis) to enable rapid bootstrapping of new side projects.

See [.plans/tiny-app-platform-v1.md](.plans/tiny-app-platform-v1.md) for the full execution plan.

## Prerequisites

### Local development

Required to build and run any app built on this platform (`func start`, `dotnet run`, migrations) on your own machine:

| Dependency | Why | Notes |
|---|---|---|
| .NET SDK 10.0+ | Build/run target framework | |
| Node 24+ | Front-end client packages, `dotnet new` template tooling | |
| Azure Functions Core Tools v4 (`func` CLI) | Runs the Functions host locally | |
| Azure CLI (`az`) | Deploying infra/bicep, zip-deploying Function Apps | |
| **Azurite** (`npm install -g azurite`) | Local Azure Storage emulator | **Required**, not optional — every app's `local.settings.json` sets `AzureWebJobsStorage: "UseDevelopmentStorage=true"`, and the Functions host's own health checks depend on reaching it. Without Azurite running, the host repeatedly fails its internal storage health check and the HTTP listener never comes up (looks like a silent hang or a 404/503, not an obvious storage error). Start it before `func start`: `azurite --location <some-data-dir>`. Visual Studio's F5 debug flow starts this automatically via `serviceDependencies.local.json`; the CLI (`func start`, `dotnet run`) does not, so start it manually. |
| SQL Server LocalDB | Local database for migrations/dev | Comes with Visual Studio, or install "SQL Server Express LocalDB" standalone. Verify with `sqllocaldb info`. |

### Azure (per-app runtime)

Each app deployed via `infra/app.bicep` provisions and depends on:

| Resource | Purpose |
|---|---|
| Function App (Consumption/Y1 plan) | Hosts the app's Functions |
| Azure SQL Database | Per-app database on the shared SQL server (`pschop-db`) |
| User-assigned Managed Identity | Auth to SQL and Storage — no connection-string secrets |
| Storage Account blob container | Serves the SPA static content; shared account (`stockinfostorage`), per-app container |
| Azure OpenAI account (optional) | Only if the app uses `PS.AppPlatform.Llm` |

App Service **Easy Auth must stay disabled** (the bicep asserts this) — the platform authenticates in-process. See [docs/provisioning.md](docs/provisioning.md) for the full provisioning runbook.
