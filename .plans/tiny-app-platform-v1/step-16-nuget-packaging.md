# Step 16 â€” NuGet packaging

**Phase:** 3 â€” Packaging
**Depends on:** Step 15 (Gate A must have passed)
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Make both packages produce correct `.nupkg` files, and prove that a consumer works through a
**`PackageReference`** â€” which is the path that actually exercises the `build/*.targets`
injection. Everything so far has used `ProjectReference`, which bypasses it.

## Why this step is more than `dotnet pack`

The Step 14 sample includes the shims with an explicit `<Compile Include>`. A real app gets
them from the auto-imported targets file. Those are different mechanisms, and only this step
tests the one customers use. If the targets file is wrong, everything still builds here and
fails in the first real app.

## Tasks

### 1. Package metadata

Add to `Directory.Build.props` (applies to both packable projects):

```xml
<PropertyGroup Condition="'$(IsPackable)' == 'true'">
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <PackageTags>azure-functions;spa;background-tasks;platform</PackageTags>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
</PropertyGroup>
```

Add `Microsoft.SourceLink.GitHub` to `Directory.Packages.props` and reference it from both
packable projects with `PrivateAssets="all"`. Source Link costs nothing and makes debugging
into the package possible, which is the difference between a package being usable and being
a black box.

### 2. Per-package README

Each packable project needs its own `README.md` packed at the root:

```xml
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

`src/PS.AppPlatform/README.md` â€” what it is, the nine-line `Program.cs`, the configuration
sections it reads, and a link to the repo docs.

`src/PS.AppPlatform.Functions/README.md` â€” the Â§2.1 explanation (why the shims are source),
the list of functions and routes it contributes, `PSAppPlatformInjectEndpoints=false` as
the opt-out, and the ProjectReference caveat from Step 13.

### 3. Version from one place

In `Directory.Build.props`, keep `<VersionPrefix>0.1.0</VersionPrefix>` and add:

```xml
<PropertyGroup>
  <VersionSuffix Condition="'$(VersionSuffix)' == '' and '$(GITHUB_ACTIONS)' != 'true'">local</VersionSuffix>
</PropertyGroup>
```

so local packs are `0.1.0-local` and can never be mistaken for a published build.

### 4. `PS.AppPlatform.Functions` depends on `PS.AppPlatform`

Its `ProjectReference` must become a **package dependency** when packed â€” the default for a
`ProjectReference` between two packable projects, so this should already work. Verify it in
the check below rather than assuming.

### 5. Pack both

```powershell
cd C:\Dev\AppPlatform
Remove-Item artifacts -Recurse -Force -ErrorAction SilentlyContinue
dotnet pack -c Release -o artifacts
```

**Expected:** `PS.AppPlatform.0.1.0-local.nupkg`,
`PS.AppPlatform.Functions.0.1.0-local.nupkg`, and two `.snupkg` files.
`SampleApp` and the test project must **not** produce packages â€” both set `IsPackable=false`.

### 6. Inspect the package contents

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Show-Nupkg($p) {
  [IO.Compression.ZipFile]::OpenRead((Resolve-Path $p)).Entries |
    Select-Object -ExpandProperty FullName | Sort-Object
}
Show-Nupkg artifacts\PS.AppPlatform.0.1.0-local.nupkg
Show-Nupkg artifacts\PS.AppPlatform.Functions.0.1.0-local.nupkg
```

**Expected for `PS.AppPlatform`:** `lib/net10.0/PS.AppPlatform.dll` and `.xml`,
`README.md`. The embedded `.sql` scripts are inside the dll as resources, so they do **not**
appear as entries.

**Expected for `PS.AppPlatform.Functions`:** `endpoints/*.cs` (all six shim files),
`build/PS.AppPlatform.Functions.targets`, `README.md`, and **no `lib/` folder at all**.

A `lib/` folder in the Functions package means the shims got compiled into an assembly and
would be invisible to worker indexing â€” the exact Â§2.1 failure. Stop and fix
`IncludeBuildOutput` if you see one.

### 7. The real test â€” consume from a local feed

This is the point of the step.

```powershell
cd C:\Dev\AppPlatform
New-Item -ItemType Directory -Force localfeed | Out-Null
Copy-Item artifacts\*.nupkg localfeed\

$tmp = Join-Path $env:TEMP "PkgConsumerTest"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $tmp | Out-Null
Copy-Item samples\SampleApp\* $tmp -Recurse
```

In `$tmp\SampleApp.csproj`, make three changes:

1. **Remove** the `<ProjectReference>` to `PS.AppPlatform`.
2. **Remove** the `<Compile Include="..\..\src\PS.AppPlatform.Functions\endpoints\*.cs" />`
   item group entirely â€” the targets file must supply these now.
3. **Add**, with explicit versions since it is outside the repo's central package management:

```xml
<PackageReference Include="PS.AppPlatform" Version="0.1.0-local" />
<PackageReference Include="PS.AppPlatform.Functions" Version="0.1.0-local" />
```

Also add `$tmp\nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="C:\Dev\AppPlatform\localfeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

And a `Directory.Build.props` that disables central package management (`false`) so the
explicit versions above are used.

Then:

```powershell
cd $tmp
dotnet build -c Debug
Get-Content obj\Debug\net10.0\functions.metadata | ConvertFrom-Json |
    Select-Object name, scriptFile | Format-Table -AutoSize
```

**Expected:** it builds, and `functions.metadata` lists the same platform functions as
Check 1 of Step 15, with `"scriptFile": "SampleApp.dll"`.

**If the platform functions are missing here but were present in Step 15**, the `.targets`
file is not being imported or its relative path is wrong. That is the single most likely
failure in this step, and the whole reason it exists. Check that `build/` is spelled exactly
`build` and the file is named exactly `PS.AppPlatform.Functions.targets`.

Optionally run `func start` in `$tmp` and repeat Step 15 Check 4 to confirm the packaged path
serves correctly end to end.

Clean up `$tmp` afterwards. Add `localfeed/` and `artifacts/` to `.gitignore`.

### 8. Note the API-surface obligation

Add a short `docs/versioning.md`:
- SemVer, `VersionPrefix` in `Directory.Build.props` is the single source
- adding a shim to `endpoints/` is a **minor** bump and reaches apps on upgrade with no code
  change â€” that is the payoff of source injection over scaffolding
- changing an `IXxxEndpoints` interface is **breaking**, because the injected shims of an
  older package version may still be in an app's compilation
- the two packages version and release together

That third point is the subtle one: an app can end up with `PS.AppPlatform 0.3.0` and
`PS.AppPlatform.Functions 0.2.0` and get a compile error from injected source. Consider
adding a version-equality check in the targets file that warns on mismatch.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build -c Release
dotnet test
dotnet pack -c Release -o artifacts
```

Plus the local-feed consumer test from task 7.

## Done when

- [ ] Both packages pack; `SampleApp` and tests do not
- [ ] `PS.AppPlatform.Functions` has **no** `lib/` folder
- [ ] `endpoints/*.cs` and `build/*.targets` are in the Functions package
- [ ] **A `PackageReference`-only consumer builds and shows the platform functions in
      `functions.metadata`** â€” the targets injection works
- [ ] Symbol packages and Source Link are produced
- [ ] `docs/versioning.md` records the two-package lockstep rule

## Commit

```powershell
git add -A
git commit -m "Step 16: NuGet packaging; verified targets-based shim injection via a PackageReference consumer"
```

