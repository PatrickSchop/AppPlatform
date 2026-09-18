# PS.AppPlatform.Templates

`dotnet new` templates for creating PS.AppPlatform applications.

## Installation

```powershell
dotnet new install PS.AppPlatform.Templates
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

- `--PlatformVersion` â€” Version of PS.AppPlatform packages to reference (default: 0.1.0)
- `--AppRole` â€” Entra app role required for authentication (default: none, auth only)
- `--Frontend` â€” Front-end starter to include: none, angular, or react (default: none)
- `--SqlServer` â€” SQL Server logical server name (default: pschop-db)
- `--StorageAccount` â€” Storage account for background job coordination (default: stockinfostorage)

## See also

- [PS.AppPlatform source](https://github.com/PatrickSchop/AppPlatform)
- [Template README](content/tinyapp/README.md)

