# Wisdi.AppPlatform.Functions

Source-injected `[Function]` shims for the Wisdi.AppPlatform backend.

## Architecture

This package produces **no assembly**. It ships only:

- `endpoints/*.cs` — plain injectable service shims with `[Function]` attributes
- `build/Wisdi.AppPlatform.Functions.targets` — auto-imported MSBuild targets

The targets file injects the loose `.cs` files into the consumer's compilation so that the
Azure Functions worker's Roslyn source generator can see the `[Function]` methods.

See `tiny-app-platform-v1.md` §4.1 for why this approach is necessary.

## ProjectReference vs PackageReference trap

⚠️ **Important:** NuGet's auto-import of `build/*.targets` works **only** for `<PackageReference>`.
A `<ProjectReference>` does **not** flow the `.targets` file.

### Consequence 1: Local development with ProjectReference

When consuming via `<ProjectReference>` (e.g., `samples/SampleApp`), manually include the shims:

```xml
<ItemGroup>
  <Compile Include="..\..\src\Wisdi.AppPlatform.Functions\endpoints\*.cs" Visible="false" />
</ItemGroup>
```

### Consequence 2: Verification at Step 16

The `.targets` auto-import is only exercised when the package is consumed from a NuGet feed
(local or remote). This is tested at Step 16 (packing) and Step 19 (template verification).

A typo in the targets file will not surface during Steps 01–14 because the sample app uses
`<ProjectReference>`, which does not load the targets. **Do not assume the shims compile
correctly until Step 14 integrates them.**

## Building

No special build steps — this project produces a content package only.

```powershell
dotnet build src\Wisdi.AppPlatform.Functions\Wisdi.AppPlatform.Functions.csproj
```

## Packing

```powershell
dotnet pack src\Wisdi.AppPlatform.Functions\Wisdi.AppPlatform.Functions.csproj -o artifacts
```

Verify the package contents:

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::OpenRead((Resolve-Path artifacts\Wisdi.AppPlatform.Functions.*.nupkg)).Entries | Select-Object FullName
```

**Must contain:** `endpoints/*.cs` and `build/Wisdi.AppPlatform.Functions.targets`.
**Must NOT contain:** `lib/net10.0/Wisdi.AppPlatform.Functions.dll` or any `lib/` folder.

If a `lib/` folder appears, `IncludeBuildOutput` did not take effect and the shims will be
invisible at runtime.

## Disabling auto-injection

Apps that want to hand-write their own `[Function]` shims can disable auto-injection:

```xml
<PropertyGroup>
  <WisdiAppPlatformInjectEndpoints>false</WisdiAppPlatformInjectEndpoints>
</PropertyGroup>
```

## Configuration

The `TaskSchedulerFunctions` timer uses a configurable schedule:

```
[TimerTrigger("%backgroundTasks:checkSchedule%")]
```

This reads from app settings, so provide the default in `local.settings.json`:

```json
{
  "Values": {
    "backgroundTasks:checkSchedule": "0 */5 * * * *"
  }
}
```

The timer runs every 5 minutes (CRON: `0 */5 * * * *`). This can be tuned per deployment.
