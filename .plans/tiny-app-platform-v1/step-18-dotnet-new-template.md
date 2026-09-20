# Step 18 â€” `dotnet new tinyapp` template

**Phase:** 4 â€” Template and infrastructure
**Depends on:** Step 17
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Package the scaffolding for a new app as a `dotnet new` template, so a new project is one
command rather than an afternoon of copying.

## What it scaffolds

Everything an app needs and nothing it does not: `Program.cs`, the csproj, appsettings
layering, a derived `AppDbContext`, `Database/Scripts/100_InitialSchema.sql`, `host.json`,
`local.settings.json`, a deploy workflow, and the bicep parameter file.

`samples/SampleApp` is the working reference. The template is essentially that app with the
Note domain removed and the names parameterised.

## Tasks

### 1. Layout

```
templates/
  PS.AppPlatform.Templates.csproj
  content/
    tinyapp/
      .template.config/
        template.json
        dotnetcli.host.json
        ide.host.json
      TinyApp.csproj
      Program.cs
      AppServiceBuilder.cs
      Data/AppDbContext.cs
      Database/Scripts/100_InitialSchema.sql
      appsettings.json
      appsettings.development.json
      appsettings.deployment.json
      host.json
      local.settings.json
      .gitignore
      README.md
      .github/workflows/deploy.yaml
```

**Post-Step-20 revision:** no `infra/` folder ships in the generated app. Provisioning
(`infra/app.bicep`) turned out to be a one-time operation run from the `PS.AppPlatform` repo
itself (see `docs/provisioning.md`), not something the generated app's own CI/CD invokes on
every push â€” so `infra/main.bicepparam`, and the `SqlServer`/`StorageAccount` symbols that only
existed to populate it, were dropped. `deploy.yaml` only builds and deploys application code
against Azure resources provisioned separately.

### 2. `templates/content/tinyapp/.template.config/template.json`

```json
{
  "$schema": "http://json.schemastore.org/template",
  "author": "PS",
  "classifications": ["Cloud", "Serverless", "Web", "Azure Functions"],
  "identity": "PS.AppPlatform.TinyApp",
  "name": "PS tiny app",
  "shortName": "tinyapp",
  "description": "An Azure Functions app on PS.AppPlatform: SPA hosting, background tasks, Entra auth and script migrations.",
  "tags": { "language": "C#", "type": "project" },
  "sourceName": "TinyApp",
  "preferNameDirectory": true,
  "symbols": {
    "PlatformVersion": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "0.1.0",
      "replaces": "0.0.0-PLATFORM-VERSION",
      "description": "PS.AppPlatform package version."
    },
    "AppRole": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "",
      "replaces": "APP-ROLE-PLACEHOLDER",
      "description": "Entra App Role required to use this app, e.g. recipes.user. Leave empty to require authentication only."
    },
    "AppName": {
      "type": "parameter",
      "datatype": "string",
      "defaultValue": "tinyapp",
      "replaces": "AZURE-APP-NAME-PLACEHOLDER",
      "description": "Lowercase Azure resource name id used by infra/app.bicep's appName param when this app was provisioned (e.g. 'scratchapp'). Must match what was actually deployed."
    },
    "Frontend": {
      "type": "parameter",
      "datatype": "choice",
      "defaultValue": "none",
      "choices": [
        { "choice": "none",    "description": "Backend only" },
        { "choice": "angular", "description": "Include the Angular starter" },
        { "choice": "react",   "description": "Include the React starter" }
      ],
      "description": "Front-end starter to include."
    },
    "skipRestore": { "type": "parameter", "datatype": "bool", "defaultValue": "false" }
  },
  "sources": [
    {
      "modifiers": [
        { "condition": "(Frontend != 'angular')", "exclude": ["WebApp/**"] },
        { "condition": "(Frontend != 'react')",   "exclude": ["WebApp-React/**"] }
      ]
    }
  ],
  "postActions": [
    {
      "condition": "(!skipRestore)",
      "description": "Restore NuGet packages",
      "manualInstructions": [{ "text": "Run 'dotnet restore'" }],
      "actionId": "210D431B-A78B-4D2F-B762-4ED3E3EA9025",
      "continueOnError": true
    }
  ]
}
```

`sourceName: "TinyApp"` means every occurrence of `TinyApp` â€” filenames, namespaces, the
csproj name â€” becomes the `-n` value.

The `Frontend` choice depends on Steps 26 and 27. Set it up now but leave the
`WebApp/`/`WebApp-React/` content out; Step 27 adds it and updates the default.

### 3. Template content

Copy from `samples/SampleApp`, then:

- **Remove** everything Note-related: `Data/Note.cs`, `Tasks/`, `Api/`,
  `wwwroot/`, the Notes lines in `AppServiceBuilder`.
- `AppDbContext` keeps only the base â€” no `DbSet`s, with a comment showing how to add one.
- `AppServiceBuilder` is a stub with both overrides present but empty, each with a one-line
  comment showing what goes there. An empty method with a worked comment teaches more than
  an absent one.
- `100_InitialSchema.sql` is a commented-out `CREATE TABLE` example plus a header explaining
  the `000-099` core / `100+` app numbering rule.
- `TinyApp.csproj` uses `PackageReference` with `0.0.0-PLATFORM-VERSION`, and **no**
  `<Compile Include>` for the shims â€” the targets file does that. Add a comment saying so,
  since its absence is otherwise mysterious.
- `appsettings.json` gets an `authentication` section with
  `"requiredRole": "APP-ROLE-PLACEHOLDER"` and empty tenant/client ids, plus a comment
  pointing at `docs/auth-setup.md`. **No secrets, and no real GUIDs.**
- `README.md` is a real getting-started: prerequisites, create the database, `--migrate`,
  `func start`, add an entity, add a task handler, deploy. Keep it under a page.

### 4. `templates/PS.AppPlatform.Templates.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>PS.AppPlatform.Templates</PackageId>
    <PackageType>Template</PackageType>
    <Title>PS tiny app templates</Title>
    <Description>dotnet new templates for PS.AppPlatform.</Description>
    <IncludeContentInPack>true</IncludeContentInPack>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <ContentTargetFolders>content</ContentTargetFolders>
    <NoWarn>$(NoWarn);NU5128</NoWarn>
    <EnableDefaultItems>false</EnableDefaultItems>
    <IsPackable>true</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="content\**\*" Exclude="content\**\bin\**;content\**\obj\**" />
    <Compile Remove="**\*" />
  </ItemGroup>
</Project>
```

`EnableDefaultItems=false` and `<Compile Remove="**\*" />` are both needed â€” otherwise MSBuild
tries to compile the template's `Program.cs` as part of this project and fails on the
placeholder tokens.

**Do not add this project to the solution.** Its content is not compilable, and including it
makes `dotnet build` at the repo root fail. Pack it explicitly instead.

### 5. Exclude template content from the repo build

Add to `Directory.Build.props`:

```xml
<PropertyGroup Condition="$(MSBuildProjectDirectory.Contains('templates\content'))">
  <ExcludeFromSolutionBuild>true</ExcludeFromSolutionBuild>
</PropertyGroup>
```

Simpler and more reliable: name the template csproj `TinyApp.csproj.template` in source and
have the template config rename it. If MSBuild keeps picking up template content during a
root build, take that route.

### 6. Pack and install locally

```powershell
cd C:\Dev\AppPlatform
dotnet pack templates\PS.AppPlatform.Templates.csproj -c Release -o artifacts
dotnet new uninstall PS.AppPlatform.Templates 2>$null
dotnet new install artifacts\PS.AppPlatform.Templates.0.1.0-local.nupkg
dotnet new list tinyapp
```

**Expected:** `tinyapp` is listed with the "PS tiny app" name.

### 7. Add to the publish workflow

Add the templates project to `dotnet pack` in `.github/workflows/publish.yaml`, so it releases
in lockstep with the packages it references.

## Verification

```powershell
$t = Join-Path $env:TEMP "TemplateSmoke"
Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $t | Out-Null
cd $t
dotnet new tinyapp -n SmokeApp --PlatformVersion 0.1.0-local --AppRole smoke.user
Get-ChildItem -Recurse -File | Select-Object -ExpandProperty FullName
```

**Expected:**
- A `SmokeApp` folder with `SmokeApp.csproj` and `Program.cs`
- **No `TinyApp` string anywhere** â€” check with
  `Select-String -Path .\SmokeApp\* -Pattern 'TinyApp' -Recurse`, which must return nothing
- `appsettings.json` has `"requiredRole": "smoke.user"`
- `SmokeApp.csproj` references `PS.AppPlatform 0.1.0-local`
- No `0.0.0-PLATFORM-VERSION` or `*-PLACEHOLDER` token survives anywhere

Building it is Step 19 â€” that is the actual gate.

## Done when

- [ ] `dotnet new list tinyapp` shows the template
- [ ] Scaffolding to a new name leaves no `TinyApp` or placeholder token behind
- [ ] The template does not break the root `dotnet build`
- [ ] Generated `appsettings.json` contains no secret and no real GUID
- [ ] The generated README is a usable getting-started page
- [ ] The templates package is in the publish workflow

## Commit

```powershell
git add -A
git commit -m "Step 18: dotnet new tinyapp template"
```

