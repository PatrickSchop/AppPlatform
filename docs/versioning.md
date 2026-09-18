# Versioning Policy

## Overview

Both packages (`Wisdi.AppPlatform` and `Wisdi.AppPlatform.Functions`) use **semantic versioning** and are released together as a lockstep.

## Version Source

`VersionPrefix` in `Directory.Build.props` is the single source of truth for version numbers. All packages derive their version from this property.

Local builds append `-local` suffix (via `VersionSuffix`), so local packages are never confused with published releases.

## Release Process

- **Patch (0.x.Z)**: Bug fixes to platform code, schema migration fixes. No API surface changes.
- **Minor (0.Y.0)**: New endpoints added to `endpoints/` directory, new public services. **Consuming apps pick up new shims on upgrade without code changes** — this is the payoff of source injection over code generation.
- **Major (X.0.0)**: Breaking changes to public interfaces (`IXxxEndpoint` contracts, endpoint signatures, data layer conventions).

## The Lockstep Rule: Critical

**Both packages must be released at the same version number.**

### Why

An app's `packages.lock.json` can contain:
```json
"Wisdi.AppPlatform": "0.3.0",
"Wisdi.AppPlatform.Functions": "0.2.0"
```

If the Functions package was injected at version 0.2.0 but the platform was upgraded to 0.3.0, a breaking change in 0.3.0's public interface will cause **compilation errors in the injected source**.

### Enforcement

The MSBuild targets file can validate version equality at build time:

```xml
<Target Name="ValidatePlatformVersions" BeforeTargets="Build">
  <Error Condition="'$(Wisdi_AppPlatform_Version)' != '$(Wisdi_AppPlatform_Functions_Version)'"
         Text="Wisdi.AppPlatform and Wisdi.AppPlatform.Functions must have the same version." />
</Target>
```

(Not yet implemented; consider adding if version mismatches become common.)

## Publishing

1. Update `VersionPrefix` in `Directory.Build.props`
2. Run `dotnet pack -c Release -o artifacts`
3. Push both `.nupkg` files to NuGet.org with the same version

The GitHub Actions publish workflow enforces this by packing both projects in one action.
