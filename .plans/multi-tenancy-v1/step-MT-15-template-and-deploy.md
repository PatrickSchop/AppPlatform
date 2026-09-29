# Step MT-15 — Template `--Tenancy`, deploy-time registration, docs, release

**Phase:** 3 — Front-end and template
**Depends on:** MT-14
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Make a multi-tenant app a `dotnet new` option, register it automatically on every deploy, and
publish the packages so a generated app gets all of it from NuGet.

## Tasks

### 1. Template symbol (`templates/content/tinyapp/.template.config/template.json`)

```json
"Tenancy": {
  "type": "parameter",
  "datatype": "choice",
  "defaultValue": "none",
  "choices": [
    { "choice": "none",   "description": "Authentication only, no registry (v1 behaviour)" },
    { "choice": "single", "description": "Registered users and roles, one tenant" },
    { "choice": "multi",  "description": "Registered users and roles, several tenants with tenant selection" }
  ],
  "description": "Tenancy model. single/multi require the management app."
},
"TenancyEnabled": { "type": "computed", "value": "(Tenancy != \"none\")" },
"TenancyMode": {
  "type": "generated", "generator": "switch", "replaces": "TENANCY-MODE-PLACEHOLDER",
  "parameters": { "evaluator": "C++", "datatype": "string", "cases": [
    { "condition": "(Tenancy == \"single\")", "value": "Single" },
    { "condition": "(Tenancy == \"multi\")",  "value": "Multi" },
    { "condition": "(true)",                  "value": "None" } ] }
}
```

Mention it in `dotnetcli.host.json` and the template README.

### 2. Template content, conditional on `TenancyEnabled`

| File | Change |
|---|---|
| `TinyApp.csproj` | `<PlatformTenancy>true</PlatformTenancy>` (inside `#if (TenancyEnabled)`) |
| `Program.cs` | `builder.Services.AddPlatformTenancy(builder.Configuration);` and `PlatformCommandLine` (MT-01) |
| `Manifest.cs` (new; excluded when `none`) | `Key => "AZURE-APP-NAME-PLACEHOLDER"` (reuses the `AppName` symbol), `Tenancy => TenancyMode.TENANCY-MODE-PLACEHOLDER`, roles `editor`, `viewer` |
| `Data/Item.cs` (new; excluded when `none`) | `public class Item : TenantEntity { public string Name { get; set; } = ""; }` |
| `Database/Scripts/110_CreateItems.sql` (excluded when `none`) | table per the `docs/multi-tenancy.md` template |
| `Api/ItemsEndpoints.cs` + shim (excluded when `none`) | `GET` (`[Authorize]`), `POST` (`[Authorize(Roles="editor")]`) |
| `appsettings.json` | `"tenancy": { "mode": "TENANCY-MODE-PLACEHOLDER", "directory": "management" }` |
| `appsettings.development.json` | `"directory": "config"` + a `devDirectory` with one tenant and a placeholder user, plus a comment block explaining how to fill in `oid`/`tid` |
| `.github/workflows/deploy.yaml` | pass `register: true` and the management URL/audience (task 3) |
| `README.md`, `docs/deployment.md` | a "Tenancy" section: what the option did, and the one-time registry setup (trusted deployer) |

Use `sources.modifiers` exclusions for whole files and `//#if (TenancyEnabled)` blocks inside
shared files, in the style of the existing `Frontend` symbol.

When `Frontend` is `angular` or `react`, the starter copy already contains the MT-14 wiring
and needs no template change: it switches on `/configuration.json` at runtime.

### 3. Deploy-time registration (`.github/workflows/app-deploy.yaml`)

New inputs:

```yaml
register:
  required: false
  type: boolean
  default: false
  description: 'Register the app and its roles with the management app (tenancy single/multi)'
management_url:
  required: false
  type: string
  default: ''
management_audience:
  required: false
  type: string
  default: ''
```

New step after "Remove SQL firewall rule" and before "Deploy Function App", so a registration
failure stops the deploy **before** new code, which may reference new roles, goes live:

```yaml
- name: Register with management app
  if: ${{ inputs.register }}
  env:
    tenancy__management__url: ${{ inputs.management_url }}
    tenancy__management__audience: ${{ inputs.management_audience }}
  run: |
    PRINCIPAL_ID=$(az identity show -g "${{ inputs.resource_group }}" -n "id-${{ inputs.app_name }}" --query principalId -o tsv)
    cd app-output
    dotnet "${{ inputs.assembly_name }}.dll" --register --principal-id "$PRINCIPAL_ID"
```

- Fail with a clear message when `register` is true and either URL or audience is empty.
- `az identity show` works because the job is already logged in; `id-<app>` is the bicep
  naming (`infra/app.bicep`).
- `actionlint` runs in CI. Keep it green.

### 4. Infrastructure

`infra/app.bicep` gains optional params `managementUrl` and `managementAudience`. When
non-empty, it emits `tenancy__management__url` and `tenancy__management__audience` app
settings. It does not change the per-app footprint otherwise. The management app itself
(Gate E) passes neither.

### 5. `docs/multi-tenancy.md`, complete

Extends the MT-03 data section into the single reference for app authors and the operator:
1. Concepts: the Application → Tenant → Team → User → Role tree, and the three modes.
2. Quick start: `dotnet new tinyapp --Tenancy multi`, local dev with the config directory.
3. Tenant-aware data (from MT-03) and the factory table.
4. Anonymous endpoints: resolve the tenant yourself, then `CreateForTenant` (the
   `MultiTenantSample` feedback pattern).
5. Roles: declare them in `Manifest`, enforce them with `[Authorize(Roles=…)]`. Entra App
   Roles are ignored in registry apps (MT-04).
6. Login flow: `/api/me/tenants`, the header, the error codes table (D3 + `registry_unavailable`).
7. Registration: what `--register` does, the upsert rules, deprecation.
8. Operator runbook: add a deploy principal to `registry:trustedDeployers`, add the app origin
   to the SPA redirect URIs, invite users.
9. Caching and consistency: 5 min / 1 min / stale-if-error 1 h, and what that means for
   revocation.

Link it from the repository README and from `docs/auth-setup.md`.

### 6. Release

Per `docs/versioning.md`: new endpoints and public services → **minor**. Bump `VersionPrefix`
in `Directory.Build.props` to `0.2.0`, update the template's `PlatformVersion` default to
`0.2.0`, and publish both packages in lockstep through the existing publish workflow. Bump the
three npm packages' minor versions (MT-13/MT-14) and publish them the same way the v1 plan
publishes them.

## Tests to add

1. Template instantiation tests (extend whatever v1 Step 18/19 established, or add a script):
   `--Tenancy none` produces **no** `Manifest.cs`, no `PlatformTenancy` and no `tenancy`
   section; `single` and `multi` produce all of them, with the correct mode string in both the
   manifest and `appsettings.json`.
2. Each of the three generated variants runs `dotnet build` clean.
3. `actionlint` passes on the modified workflows.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release; dotnet test
dotnet new install .\templates --force
foreach ($t in 'none','single','multi') {
  $d = "$env:TEMP\tt-$t"; Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue
  dotnet new tinyapp -n TT$t -o $d --Tenancy $t --AppName "tt$t"
  dotnet build $d -c Release
}
```

**Expected:** three clean builds. Only `single`/`multi` have the tenancy files, and
`functions.metadata` for those two lists `GetMyTenants`.

## Done when

- [ ] `dotnet new tinyapp --Tenancy multi` produces a working multi-tenant app with no hand edits
      beyond the documented dev `oid`/`tid`
- [ ] Deploys register the app before the new code goes live
- [ ] `docs/multi-tenancy.md` covers every section above
- [ ] `0.2.0` published in lockstep; npm packages published

## Commit

```powershell
git add -A
git commit -m "MT-15: tinyapp --Tenancy option, deploy-time registration, multi-tenancy docs, 0.2.0"
```
