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

### Restoring always needs a token, even now the repository is public

**GitHub Packages' NuGet registry requires authentication for every read.** Making
`AppPlatform` public did not change this: an anonymous `dotnet restore` still fails with
**401 Unauthorized**, verified on 2026-09-29. Only the container registry (`ghcr.io`) serves
anonymously; npm, NuGet, Maven and RubyGems on GitHub Packages do not.

The failure is easy to misread. NuGet reports *"Your request could not be authenticated by
the GitHub Packages service"* and then a 401 — which reads like a broken token rather than
the absence of one.

**Package visibility is separate from repository visibility, and did not follow it.** As of
2026-09-29 the package metadata endpoint still refuses anonymous reads, so the package is
still private. That distinction decides which token you need:

| Package visibility | What can restore it |
|---|---|
| Private (current) | A classic PAT with `read:packages`. A consuming workflow's own `secrets.GITHUB_TOKEN` is scoped to its own repository and gets **403**. |
| Public | Any authenticated token, so a consuming workflow's `secrets.GITHUB_TOKEN` is enough. Local developers still need some token. |

To switch the package over, open it from the repository's Packages section and change its
visibility there; it is not inherited from the repository.

While the package is private, every consuming repository needs a classic PAT with
`read:packages` stored as a secret, whether or not it shares an owner with `AppPlatform`:

```yaml
name: Build

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - run: dotnet restore
      - run: dotnet build
```

### Different organization

If your consuming app is in a different GitHub organization or account, create a Personal Access Token in the `PatrickSchop` account and store it as a repository secret:

1. Create a [classic PAT](https://github.com/settings/tokens) with `read:packages` scope in the `PatrickSchop` account
2. Add it as a repository secret (e.g., `PS_PACKAGES_TOKEN`) in your consuming repo
3. Use it in your workflow:

```yaml
name: Build

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Add GitHub NuGet source
        shell: bash
        run: |
          dotnet nuget add source https://nuget.pkg.github.com/PatrickSchop/index.json \
            --name PS \
            --username PatrickSchop \
            --password ${{ secrets.PS_PACKAGES_TOKEN }} \
            --store-password-in-clear-text

      - run: dotnet restore
      - run: dotnet build
```

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

