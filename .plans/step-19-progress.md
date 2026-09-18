# Step 19: Gate B — Template Verification Progress

**Date:** 2026-09-18
**Status:** In Progress — Template Defects Fixed, Awaiting GitHub Packages Auth

## Template Defects Found and Fixed

During Check 1 (Scaffold and Build), the following template defects were discovered and fixed:

### 1. Missing TargetFramework ✅ FIXED
**Problem:** `TinyApp.csproj` had no `<TargetFramework>` property, causing NETSDK1013 error.
**Fix:** Added `Directory.Build.props` with `<TargetFramework>net10.0</TargetFramework>`

### 2. Missing Central Package Management ✅ FIXED  
**Problem:** `TinyApp.csproj` referenced packages without versions, violating the platform's central version management policy.
**Fix:** Created `Directory.Packages.props` with all package versions defined centrally.

### 3. Package Version Conflict ✅ FIXED
**Problem:** Directory.Packages.props specified `Microsoft.AspNetCore.Authentication.JwtBearer` v9.0.1, but `Microsoft.Identity.Web` v4.2.0 requires v10.0.0 or higher.
**Fix:** Updated to v10.0.1

### 4. Missing NuGet Source Configuration ✅ FIXED
**Problem:** Generated app couldn't restore `PS.AppPlatform` packages (not on public nuget.org).
**Fix:** Added `nuget.config` with:
  - GitHub Packages source for `PS.AppPlatform*`
  - NuGet.org source for other packages
  - Package source mapping to clarify which packages come from which source
  - Placeholder credentials documentation

### 5. Updated Documentation ✅ FIXED
**Problem:** README didn't mention GitHub Packages or authentication requirements.
**Fix:** Added prerequisite step documenting GitHub token setup and link to consuming-packages.md

## Test Progress

### Check 1: Scaffold and Build
- ✅ Template scaffolds without errors
- ✅ Generated project structure is correct
- ⏳ **BLOCKED:** Build requires GitHub Packages authentication
  - Packages `PS.AppPlatform` (v0.1.0) and `PS.AppPlatform.Functions` (v0.1.0) must be available
  - Users need GitHub PAT with `read:packages` scope in credentials
  - See `docs/consuming-packages.md` in platform repo for setup

### Check 2: Functions Indexed
- ⏳ Deferred: Depends on Check 1 build succeeding

### Checks 3-6: Entity, Task Handler, Endpoint, Runtime
- ⏳ Deferred: Depend on Check 1

### Check 7: Deploy to Azure
- ⏳ Deferred: Depends on Step 20 (Bicep infrastructure)

## Next Steps

1. **For immediate testing:** Need GitHub Packages access
   - Option A: Use real GitHub PAT with packages:read scope
   - Option B: Publish 0.1.0 packages to GitHub Packages
   - Option C: Create alternative local testing scenario

2. **Continue Checks 2-7** once authentication is resolved

3. **Record elapsed time** and any friction points found

4. **Create Gate B results document** with defects fixed and times

## Files Modified

- `templates/content/tinyapp/TinyApp.csproj` — removed inline TFM
- `templates/content/tinyapp/Directory.Build.props` — NEW
- `templates/content/tinyapp/Directory.Packages.props` — NEW  
- `templates/content/tinyapp/nuget.config` — NEW
- `templates/content/tinyapp/README.md` — added auth prerequisites

## Commits

- `1f7044a` Step 19: Fix template defects (TFM, central package mgmt, version conflict, nuget config)

