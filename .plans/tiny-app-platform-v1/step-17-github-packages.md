# Step 17 â€” Publish to GitHub Packages

**Phase:** 3 â€” Packaging
**Depends on:** Step 16
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Publish both packages to GitHub Packages (free for private feeds) on a tagged release, and
document how a consuming app authenticates to that feed.

## Prerequisites

- A GitHub repository exists for this project. If `git remote -v` is empty, create one:
  `gh repo create PatrickSchop/AppPlatform --private --source . --push`
- `RepositoryUrl` in `Directory.Build.props` matches the actual remote. Fix it if Step 01
  guessed wrong.

## Tasks

### 1. `.github/workflows/ci.yaml`

Runs on every push and PR. This is the guard that keeps Gate A from silently rotting.

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore
      - run: dotnet test --configuration Release --no-build --logger trx
      - run: dotnet pack --configuration Release --no-build -o artifacts

      - name: Assert the Functions package ships no assembly
        shell: pwsh
        run: |
          Add-Type -AssemblyName System.IO.Compression.FileSystem
          $pkg = Get-ChildItem artifacts/PS.AppPlatform.Functions.*.nupkg | Select-Object -First 1
          $entries = [IO.Compression.ZipFile]::OpenRead($pkg.FullName).Entries.FullName
          if ($entries -match '^lib/') { throw "Functions package contains lib/ - shims would be invisible to worker indexing" }
          if (-not ($entries -match '^endpoints/')) { throw "Functions package is missing endpoints/" }
          if (-not ($entries -match '^build/')) { throw "Functions package is missing build/*.targets" }

      - uses: actions/upload-artifact@v4
        with:
          name: packages
          path: artifacts/*
```

The inline assertion matters more than it looks: the `lib/` mistake is silent, survives all
unit tests, and only shows up as "my endpoints return 404" in someone's app weeks later.

Note `dotnet-version: '10.0.x'` â€” the source repo's workflow defaults to `9.0.x` against a
`net10.0` project (analysis Â§5). Do not repeat that here.

### 2. `.github/workflows/publish.yaml`

```yaml
name: Publish packages

on:
  release:
    types: [published]
  workflow_dispatch:
    inputs:
      version:
        description: 'Version to publish, e.g. 0.2.0'
        required: true
        type: string

permissions:
  contents: read
  packages: write

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Resolve version
        id: v
        shell: bash
        run: |
          if [ "${{ github.event_name }}" = "release" ]; then
            V="${{ github.event.release.tag_name }}"
            V="${V#v}"
          else
            V="${{ inputs.version }}"
          fi
          echo "version=$V" >> "$GITHUB_OUTPUT"

      - run: dotnet pack --configuration Release -p:Version=${{ steps.v.outputs.version }} -o artifacts

      - name: Push to GitHub Packages
        run: |
          dotnet nuget push "artifacts/*.nupkg" \
            --source https://nuget.pkg.github.com/PatrickSchop/index.json \
            --api-key ${{ secrets.GITHUB_TOKEN }} \
            --skip-duplicate
```

`-p:Version=` overrides both `VersionPrefix` and `VersionSuffix`, so a release tag produces a
clean `0.2.0` rather than `0.2.0-local`.

`--skip-duplicate` prevents a re-run of a release from failing the workflow. GitHub Packages
does not allow overwriting a published version, which is the correct behaviour â€” if a bad
version ships, publish a new one rather than trying to replace it.

### 3. Consumer authentication â€” `docs/consuming-packages.md`

GitHub Packages requires authentication even for reading a package from a private repo, which
is the single most common stumbling block. Document it properly.

**For a developer machine:**

`nuget.config` in the consuming app's repo root:

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

Credentials go in the **user-level** config, never in the repo:

```powershell
dotnet nuget update source PS `
  --username PatrickSchop `
  --password <a classic PAT with read:packages> `
  --store-password-in-clear-text `
  --configfile $env:APPDATA\NuGet\NuGet.Config
```

State plainly that `--store-password-in-clear-text` is required on Windows for this feed and
that the token must be a **classic** PAT with `read:packages` â€” fine-grained tokens do not
work with GitHub Packages for NuGet. Both of these cost people an hour if undocumented.

**For a consuming app's GitHub Actions workflow:** `secrets.GITHUB_TOKEN` works for
repositories in the same organisation or owner. For a different owner, a PAT in a repository
secret is needed. Show both.

### 4. Publish `0.1.0`

```powershell
cd C:\Dev\AppPlatform
git tag v0.1.0
git push origin v0.1.0
gh release create v0.1.0 --title "0.1.0" --notes "Initial platform extraction. Gate A passed."
```

Then watch the workflow and confirm both packages appear under the repository's Packages tab.

### 5. Verify the published feed for real

Repeat the Step 16 task-7 consumer test, but pointed at GitHub Packages rather than
`localfeed`, with `Version="0.1.0"`.

**Expected:** restore succeeds against the authenticated feed, the app builds, and
`functions.metadata` contains the platform functions.

This is the only way to know the credential story in `docs/consuming-packages.md` is correct;
if you skip it, the first real app will find out instead.

## Verification

- CI is green on `main`, including the package-shape assertion
- The publish workflow succeeded for `v0.1.0`
- Both packages are visible in GitHub Packages
- A fresh consumer restores from the published feed and shows the platform functions in
  `functions.metadata`

## Done when

- [ ] `ci.yaml` builds, tests, packs and asserts package shape on every push
- [ ] `publish.yaml` publishes on a release and by manual dispatch
- [ ] `0.1.0` is published and visible
- [ ] A consumer restoring from the **published** feed builds and indexes correctly
- [ ] `docs/consuming-packages.md` covers the classic-PAT and clear-text requirements
- [ ] No token or credential is committed anywhere

## Commit

```powershell
git add -A
git commit -m "Step 17: CI and publish workflows; 0.1.0 published to GitHub Packages"
```

