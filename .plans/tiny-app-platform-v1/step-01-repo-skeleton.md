# Step 01 — Repository skeleton

**Phase:** 0 — Foundation
**Depends on:** nothing
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Turn the empty `C:\Dev\AppPlatform` folder into a git repository with a solution file,
centralised build and package settings, and the Azure Functions Core Tools installed.
No platform code yet.

## Rules for this step

- Do **not** read or write anything under `C:\Dev\StockAnalysis` in this step.
- Do not create any `.csproj` yet — that is Step 02.

## Tasks

### 1. Initialise git

```powershell
cd C:\Dev\AppPlatform
git init -b main
```

### 2. Create `.gitignore`

Create `C:\Dev\AppPlatform\.gitignore`:

```gitignore
bin/
obj/
node_modules/
dist/
*.user
.vs/
.vscode/
artifacts/
local.settings.json
appsettings.local.json
*.nupkg
TestResults/
.env
```

### 3. Create `Directory.Build.props`

Create `C:\Dev\AppPlatform\Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>

  <PropertyGroup Label="Package identity">
    <Authors>Patrick Schop</Authors>
    <Company>Wisdi</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/PatrickSchop/AppPlatform</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <VersionPrefix>0.1.0</VersionPrefix>
  </PropertyGroup>
</Project>
```

`CS1591` (missing XML comment) is suppressed because `TreatWarningsAsErrors` plus
`GenerateDocumentationFile` would otherwise require a doc comment on every public member.

### 4. Create `Directory.Packages.props`

Versions are taken from the verified-working `C:\Dev\StockAnalysis\App\App.csproj`.
Create `C:\Dev\AppPlatform\Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup Label="Azure Functions">
    <PackageVersion Include="Microsoft.Azure.Functions.Worker" Version="2.51.0" />
    <PackageVersion Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.0.7" />
    <PackageVersion Include="Microsoft.Azure.Functions.Worker.Extensions.Http" Version="3.3.0" />
    <PackageVersion Include="Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore" Version="2.1.0" />
    <PackageVersion Include="Microsoft.Azure.Functions.Worker.Extensions.Timer" Version="4.3.1" />
  </ItemGroup>

  <ItemGroup Label="Data">
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.1" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.1" />
  </ItemGroup>

  <ItemGroup Label="Azure">
    <PackageVersion Include="Azure.Identity" Version="1.17.1" />
    <PackageVersion Include="Azure.Storage.Blobs" Version="12.27.0" />
    <PackageVersion Include="Azure.AI.OpenAI" Version="2.1.0" />
  </ItemGroup>

  <ItemGroup Label="Auth">
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Identity.Web" Version="4.3.0" />
  </ItemGroup>

  <ItemGroup Label="Test">
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.1" />
    <PackageVersion Include="NSubstitute" Version="5.3.0" />
  </ItemGroup>
</Project>
```

If `dotnet restore` later reports that one of these versions does not exist, take the
nearest existing stable version and record the substitution in the step commit message.

### 5. Create the solution

```powershell
cd C:\Dev\AppPlatform
dotnet new sln --name AppPlatform --format slnx
```

If `--format slnx` is rejected by this SDK, fall back to `dotnet new sln --name AppPlatform`
and keep the `.sln` — nothing downstream depends on the format.

### 6. Create the empty directory structure

```powershell
cd C:\Dev\AppPlatform
mkdir src, samples, templates, infra, clients, starters, tests, docs
```

### 7. Install Azure Functions Core Tools

`func` is **not** currently installed on this machine and Steps 15, 19 and 28 need it.

```powershell
npm install -g azure-functions-core-tools@4 --unsafe-perm true
```

Then open a **new** shell (the PATH change does not apply to the current one) and verify.

If npm installation fails, use winget instead:
`winget install Microsoft.Azure.FunctionsCoreTools`

### 8. Create `README.md`

A short one — name, one-sentence purpose, a pointer to `.plans/tiny-app-platform-v1.md`,
and the prerequisite list (.NET 10 SDK, Node 24, Azure Functions Core Tools v4, Azure CLI).
Do not generate a long feature document.

## Verification

```powershell
cd C:\Dev\AppPlatform
git status --short
dotnet --version
func --version
```

**Expected:**
- `git status` lists the new untracked files and does not error.
- `dotnet --version` prints `10.0.101` or later.
- `func --version` prints a `4.x` version. **If this errors, the step is not done.**

## Done when

- [ ] `C:\Dev\AppPlatform\.git` exists and the branch is `main`
- [ ] `.gitignore`, `Directory.Build.props`, `Directory.Packages.props`, `README.md` exist
- [ ] `AppPlatform.slnx` (or `.sln`) exists
- [ ] The seven empty directories exist
- [ ] `func --version` reports 4.x in a fresh shell
- [ ] Nothing under `C:\Dev\StockAnalysis` was modified

## Commit

```powershell
git add -A
git commit -m "Step 01: repository skeleton, build props, central package versions"
```
