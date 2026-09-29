# PS.AppPlatform.Templates

`dotnet new` templates for creating PS.AppPlatform applications.

## Installation

```powershell
dotnet new install PS.AppPlatform.Templates
```

## Usage

Create a new tiny app:

```powershell
dotnet new tinyapp -n MyApp --platform-version 0.1.1
```

With an Entra app role requirement:

```powershell
dotnet new tinyapp -n MyApp --platform-version 0.1.1 --app-role my-app.user
```

With React starter:

```powershell
dotnet new tinyapp -n MyApp --platform-version 0.1.1 --frontend react
```

## Options

- `-pv`, `--platform-version` — Version of PS.AppPlatform packages to reference (default: 0.1.1)
- `-ar`, `--app-role` — Entra App Role required to use the app. Leave unset for authentication
  only, which is the default and what the any-Microsoft-account model uses.
- `-fe`, `--frontend` — Front-end starter to include: none, angular, or react (default: none)
- `-aa`, `--azure-app-name` — Lowercase Azure resource name id this app is provisioned under
  (matches `appName` in `infra/app.bicep`, e.g. `scratchapp`; default: tinyapp)

## See also

- [PS.AppPlatform source](https://github.com/PatrickSchop/AppPlatform)
- [Template README](content/tinyapp/README.md)

