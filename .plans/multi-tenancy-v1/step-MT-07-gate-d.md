# Step MT-07 — Gate D: `samples/MultiTenantSample`

**Phase:** 1 — Core tenancy (gate)
**Depends on:** MT-06
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Prove Phase 1 works **through a running Functions host with a real token and a real SQL
database**, not through DI resolution. This is the v1 §5 lesson: nine defects passed
construction-only tests. It also creates the standing regression gate for tenancy, the way
`SampleApp` is the gate for v1.

## Tasks

### 1. Create `samples/MultiTenantSample`

Copy the **shape** of `samples/SampleApp` (csproj with `ProjectReference`, explicit shim
`Compile` includes, `host.json`, settings files). Do not copy its code. Add to
`AppPlatform.slnx` under `/samples/`.

The csproj includes both shim folders, since a `ProjectReference` does not flow `.targets`:

```xml
<PropertyGroup><PlatformTenancy>true</PlatformTenancy></PropertyGroup>
<ItemGroup>
  <Compile Include="..\..\src\PS.AppPlatform.Functions\endpoints\*.cs" Visible="false" />
  <Compile Include="..\..\src\PS.AppPlatform.Functions\endpoints\tenancy\*.cs" Visible="false" />
</ItemGroup>
```

**Contents**, each chosen to exercise one requirement:

| Item | Kind | Exercises |
|---|---|---|
| `Manifest` | `Multi`; roles `editor`, `viewer`, `reporter` | D2 manifest |
| `Project : TenantEntity` (`Name`, `CreatedUtc`) | tenant table | filtered, stamped |
| `Country : Entity` (`Code`, `Name`) | application-wide table | never filtered |
| `TenantAlias : Entity` (`Slug`, `TenantId`) | app-owned lookup | anonymous → tenant |
| `Feedback : TenantEntity` (`Text`) | tenant table written anonymously | `CreateForTenant` |
| `ProjectCountTaskHandler` | background task | handler runs in tenant scope |

Scripts in `Database/Scripts/`: `100_CreateProjects.sql`, `110_CreateCountries.sql`,
`120_CreateTenantAliases.sql`, `130_CreateFeedback.sql`. Tenant tables follow the
`docs/multi-tenancy.md` template (`TenantId UNIQUEIDENTIFIER NOT NULL`, index leading with
`TenantId`). Seed two countries and the aliases `contoso` and `fabrikam` in their scripts.

**Endpoints** (`Api/`, endpoint class + shim class, as in `SampleApp`):

| Route | Auth | Implementation |
|---|---|---|
| `GET /api/projects` | `[Authorize]` | `IDbContextFactory<AppDbContext>` |
| `POST /api/projects` | `[Authorize(Roles="editor")]` | same; `TenantId` not set by the code |
| `GET /api/countries` | `[Authorize]` | same factory; application-wide |
| `POST /api/public/{slug}/feedback` | `[AllowAnonymous]` + review comment | alias lookup (unscoped), then `CreateForTenant` |
| `GET /api/feedback` | `[Authorize]` | filtered |
| `GET /api/reports/projects-per-tenant` | `[Authorize(Roles="reporter")]` | `IUnscopedDbContextFactory<AppDbContext>` |
| `POST /api/projects/count` | `[Authorize]` | creates a `ProjectCountTaskHandler` task |

**Configuration** (`appsettings.development.json`): the real `authentication` ids used by
`SampleApp` (API client id, `tenantId: common`), `tenancy:mode = Multi`,
`tenancy:directory = config`, and a `devDirectory` with tenants **Contoso** and **Fabrikam**.
The operator's own identity is the one user: `editor` in Contoso, `viewer` in Fabrikam, no
`reporter`.

Get the operator's `oid`/`tid` with:

```powershell
az ad signed-in-user show --query id -o tsv          # oid
az account show --query tenantId -o tsv              # tid
```

For a personal Microsoft account the token's `tid` is `9188040d-6c67-4c5b-b112-36a304b66dad`.
Read both from a decoded token (`jwt.ms`) rather than assuming.

### 2. Run it

LocalDB + Azurite, as for Gate A:

```powershell
cd C:\Dev\AppPlatform\samples\MultiTenantSample
dotnet run -- --migrate
dotnet run -- --migrate
func start
```

Token (the Azure CLI is pre-authorized on the API registration, per the v1 progress document):

```powershell
$t = az account get-access-token --resource api://<api-client-id> --query accessToken -o tsv
$h = @{ Authorization = "Bearer $t" }
$C = "<contoso id>"; $F = "<fabrikam id>"
```

## Gate D checklist

Record every result in the progress document.

| # | Request | Expected |
|---|---|---|
| 1 | `--migrate` twice | first applies `000`–`020` + `100`–`130`; second "up to date"; tenancy column check passes |
| 2 | `functions.metadata` | lists `GetMyTenants` with `"scriptFile": "MultiTenantSample.dll"` |
| 3 | `GET /api/me/tenants` no token | 401 |
| 4 | `GET /api/me/tenants` token, no header | 200, both tenants with roles |
| 5 | `GET /api/projects` token, no header | 409 `tenant_required` |
| 6 | `POST /api/projects` header C, `{ "name": "C1" }` | 200/201 |
| 7 | `POST /api/projects` header F | 403 (viewer in Fabrikam) |
| 8 | `GET /api/projects` header C / header F | `[C1]` / `[]` |
| 9 | `GET /api/projects` header = random GUID | 403 `tenant_forbidden` |
| 10 | SQL: `SELECT TenantId FROM Projects` | C1 row has Contoso's id, stamped by the platform |
| 11 | `GET /api/countries` header F | both seeded countries |
| 12 | `POST /api/public/fabrikam/feedback` no token | 200; then `GET /api/feedback` header F shows it, header C does not |
| 13 | `GET /api/reports/projects-per-tenant` header C | 403 (no `reporter`) |
| 14 | Add `reporter` to Contoso in config, restart, repeat 13 | 200, counts for **both** tenants |
| 15 | `POST /api/projects/count` header C; poll `GET /api/tasks` header C | task reaches `Completed`, message reports Contoso's count only |
| 16 | `GET /api/tasks` header F | does not contain the Contoso task |
| 17 | Change the configured oid, restart, `GET /api/projects` header C | 403 `not_registered` |
| 18 | `SampleApp` still passes Gate A's checks | unchanged |

### 3. Automate what can be automated

Add `samples/MultiTenantSample/gate-d.ps1`, which runs checks 3–9, 11–13 and 15–16 against a
running host, given `-Token`, `-Contoso`, `-Fabrikam`. It prints PASS/FAIL per row and exits
non-zero on any failure. It is the regression script for later steps.

## Verification

The checklist above, plus:

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

## Done when

- [ ] All 18 checks pass and are recorded, with the date, in `multi-tenancy-v1-progress.md`
- [ ] `gate-d.ps1` passes against a running host
- [ ] `MultiTenantSample` builds in CI (it is in the solution)

If any check fails, fix forward in the step that owns it. Do not start Phase 2.

## Commit

```powershell
git add -A
git commit -m "MT-07: Gate D - MultiTenantSample proves tenant isolation through a running host"
```
