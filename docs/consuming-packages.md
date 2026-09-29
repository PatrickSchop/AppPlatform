# Consuming PS.AppPlatform Packages

The `PS.AppPlatform` and `PS.AppPlatform.Functions` packages are published to GitHub Packages, which requires authentication even for private repositories.

## Developer Machine Setup

### 1. Configure the NuGet feed

Create `nuget.config` in your consuming app's repository root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="PS" value="https://nuget.pkg.github.com/PatrickSchop/index.json" />
  </packageSources>
</configuration>
```

### 2. Authenticate

Run this command **once per machine** to store credentials in the user-level NuGet configuration:

```powershell
dotnet nuget update source PS `
  --username PatrickSchop `
  --password <a classic PAT with read:packages> `
  --store-password-in-clear-text `
  --configfile $env:APPDATA\NuGet\NuGet.Config
```

**Critical requirements:**

- **Classic PAT only:** Fine-grained GitHub tokens do not work with GitHub Packages for NuGet. Create a [classic Personal Access Token](https://github.com/settings/tokens) with the `read:packages` scope.
- **`--store-password-in-clear-text` is required on Windows** for this feed. Without it, `dotnet restore` will fail silently. This is a GitHub Packages limitation with NuGet on Windows; the credential must be stored plaintext in your user's `%APPDATA%\NuGet\NuGet.Config`.
- Store the token securely; it grants read access to your private packages.

### 3. Reference the packages

In your `.csproj`, add the package references:

```xml
<ItemGroup>
  <PackageReference Include="PS.AppPlatform" Version="0.1.2" />
  <PackageReference Include="PS.AppPlatform.Functions" Version="0.1.2" />
</ItemGroup>
```

Then restore and build:

```bash
dotnet restore
dotnet build
```

## GitHub Actions Workflow Setup

### Restoring needs a token, but not a PAT

**GitHub Packages' NuGet registry requires a token for every read, even for public
packages.** An anonymous `dotnet restore` fails with **401 Unauthorized** — verified on
2026-09-29 against the public `PS.AppPlatform`. Only the container registry (`ghcr.io`)
serves anonymously; npm, NuGet, Maven and RubyGems do not.

The failure is easy to misread: NuGet reports *"Your request could not be authenticated by
the GitHub Packages service"* and then a 401, which reads like a broken token rather than the
absence of one.

**In a workflow, `secrets.GITHUB_TOKEN` is enough — provided you grant it the scope.** The
token has no package access by default, and without it restore fails with **403** even though
the packages are public. A token carrying `repo` but not `read:packages` reaches the service
index and is still refused on the package itself, which is what makes this look like a
permissions bug in your own repository.

```yaml
permissions:
  contents: read
  packages: read      # without this, restore fails with 403
```

Verified end to end on 2026-09-29: a consuming repository restored `PS.AppPlatform 0.1.2`
from GitHub Packages using only its own `GITHUB_TOKEN` and that permission block.

**A classic PAT with `read:packages` is only needed for local development**, where no
workflow token exists. See *Developer Machine Setup* above.

A full workflow:

```yaml
name: Build

on: [push, pull_request]

permissions:
  contents: read
  packages: read

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Authenticate to GitHub Packages
        run: |
          dotnet nuget update source github             --username ${{ github.actor }}             --password ${{ secrets.GITHUB_TOKEN }}             --store-password-in-clear-text

      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
```

This assumes a `nuget.config` that already declares the `github` source, which the template
generates. `update source` rather than `add source` avoids failing on a source that exists.

### Consumers in another account or organization

Nothing extra is required. The packages are public, so any workflow's own `GITHUB_TOKEN`
can read them with `packages: read`; it does not need to belong to the publishing account.

While the packages were private this needed a PAT from the publishing account, because a
token is scoped to its own repository and got a 403 across repositories. That no longer
applies, and a PAT is now only useful for local development.

## Troubleshooting

**"401 Unauthorized" on restore:**
- Verify the classic PAT has the `read:packages` scope
- Regenerate and re-store the token if it's old
- On Windows, check that the token was stored with `--store-password-in-clear-text`

**"Package not found":**
- Verify the version exists in [GitHub Packages](https://github.com/PatrickSchop/AppPlatform/packages)
- Ensure you're referencing the correct package name: `PS.AppPlatform` or `PS.AppPlatform.Functions`

**Fine-grained tokens don't work:**
- GitHub Packages only supports classic PATs for NuGet. Create one with the `read:packages` scope.

