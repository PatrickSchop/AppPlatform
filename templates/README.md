# Wisdi.AppPlatform.Templates

`dotnet new` templates for creating Wisdi.AppPlatform applications.

## Installation

```powershell
dotnet new install Wisdi.AppPlatform.Templates
```

## Usage

Create a new tiny app:

```powershell
dotnet new tinyapp -n MyApp --PlatformVersion 0.1.0
```

With an Entra app role requirement:

```powershell
dotnet new tinyapp -n MyApp --PlatformVersion 0.1.0 --AppRole my-app.user
```

With React starter:

```powershell
dotnet new tinyapp -n MyApp --PlatformVersion 0.1.0 --Frontend react
```

## Options

- `--PlatformVersion` — Version of Wisdi.AppPlatform packages to reference (default: 0.1.0)
- `--AppRole` — Entra app role required for authentication (default: none, auth only)
- `--Frontend` — Front-end starter to include: none, angular, or react (default: none)
- `--SqlServer` — SQL Server logical server name (default: pschop-db)
- `--StorageAccount` — Storage account for background job coordination (default: stockinfostorage)

## See also

- [Wisdi.AppPlatform source](https://github.com/PatrickSchop/AppPlatform)
- [Template README](content/tinyapp/README.md)
