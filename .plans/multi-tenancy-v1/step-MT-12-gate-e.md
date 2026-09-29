# Step MT-12 — Gate E: the management app, deployed and bootstrapped

**Phase:** 2 — Management app (gate)
**Depends on:** MT-11
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Deploy the management app the way every platform app is deployed (`infra/app.bicep` + the
reusable workflows), provision its first admin **during deployment**, and prove the whole
registry loop with two real Microsoft identities.

## Tasks

### 1. Infrastructure

Deploy with the existing template. No management-specific bicep:

```powershell
az group create -n Management -l westeurope
az deployment group create -g Management -f infra/app.bicep -p appName=management
```

Then add the app settings that are specific to it (Function App → Configuration, or `az
functionapp config appsettings set`). **Use `__` separators**, as the comment in `app.bicep`
warns:

| Setting | Value |
|---|---|
| `authentication__azureEntraId__tenantId` | `common` |
| `authentication__azureEntraId__clientId` | PS Apps API client id |
| `authentication__azureEntraId__additionalAudiences__0` | `api://<PS Apps API client id>` |
| `authentication__requiredRole` | `admin` |
| `webApp__auth__clientId` / `__tenantId` / `__scopes__0` | PS Apps SPA id / `common` / `api://<api id>/access_as_user` |
| `registry__trustedTenantId` | the operator's Entra tenant id |
| `registry__trustedDeployers__0` | object id of the GitHub OIDC deploy principal |
| `registry__publicBaseUrl` | `https://management-api.azurewebsites.net` |

Add `https://management-api.azurewebsites.net` to the **PS Apps SPA** redirect URIs
(`docs/auth-setup.md` Part 2.3).

**Token version check.** The API registration's manifest must have
`"requestedAccessTokenVersion": 2`. Personal-account sign-in already requires this. Managed
identity tokens for the same audience are then v2 as well, and Microsoft.Identity.Web
validates them with the `common` configuration. If a managed-identity call later fails with
an issuer error, this setting is the first thing to check. Record what you observe.

### 2. Workflow: bootstrap during deployment

Add an optional input to `.github/workflows/app-deploy.yaml`:

```yaml
post_migrate_args:
  required: false
  type: string
  default: ''
  description: 'Extra command run after --migrate, e.g. "--bootstrap-admin --oid … --tid … --name …"'
```

and a step directly after "Run migrations", inside the firewall window:

```yaml
- name: Post-migration command
  if: ${{ inputs.run_migrations && inputs.post_migrate_args != '' }}
  run: |
    cd app-output
    dotnet "${{ inputs.assembly_name }}.dll" ${{ inputs.post_migrate_args }}
```

Create `.github/workflows/management-deploy.yaml` in this repository. It calls `app-build.yaml`
(backend `apps/Management`, frontend `apps/Management/WebApp`) and `app-deploy.yaml` with
`assembly_name: Management`, `deploy_frontend: true`, and:

```yaml
post_migrate_args: >-
  --bootstrap-admin --oid ${{ vars.BOOTSTRAP_ADMIN_OID }}
  --tid ${{ vars.BOOTSTRAP_ADMIN_TID }} --name "${{ vars.BOOTSTRAP_ADMIN_NAME }}"
```

The ids are **repository variables, not secrets** (container plan D5). They are identifiers
rather than credentials, and reusable-workflow `with:` cannot read the `secrets` context.

The deploy principal needs `db_owner` on the new `management` database, the same as for any
app (`infra/sql-user.sql`; see `docs/provisioning.md`).

### 3. Run it

Push, run `management-deploy`, and watch the migrate + bootstrap output in the job log.

## Gate E checklist

Identity **P** = the operator (bootstrap admin). Identity **Q** = a second Microsoft account
(a personal account is fine; it proves the `common` audience).

| # | Action | Expected |
|---|---|---|
| 1 | Workflow run | migrate applies `100_CreateRegistry`; self-registration reports `Created: true`; bootstrap prints the admin it created. Re-run: no changes anywhere |
| 2 | `GET /api/health`, `/configuration.json` anonymous | 200; config has `auth` and `tenancy.mode = Single` |
| 3 | `GET /api/admin/applications` without a token | 401 |
| 4 | P signs in to the UI | `/applications` lists `management` |
| 5 | P creates user "Q", adds Q to `management`/Default/Default **with no role**, creates an invite link | link shown once |
| 6 | Q opens the link in a private window, signs in, accepts | success page |
| 7 | Q opens `/applications` | "No access" page (no `admin`), showing Q's oid/tid |
| 8 | Q calls `GET /api/admin/applications` with their token | 403 |
| 9 | P gives Q `admin` in the UI; Q reloads within ~1 min | Q sees `/applications` (null-cache expiry, MT-09) |
| 10 | P tries to remove P's and Q's admin in turn | the second removal → `last_admin` |
| 11 | The invite link opened again by anyone | "already used" |
| 12 | `az webapp auth show` | Easy Auth disabled (v1 §4.1a) |

## Verification

The checklist, with results and date recorded in `multi-tenancy-v1-progress.md`. CI stays
green: `dotnet test` and the management `npm test` run in `ci.yaml` (add them if needed).

## Done when

- [ ] The first admin exists purely because of the deployment. No manual SQL, no portal steps
      beyond app settings and the redirect URI
- [ ] A second identity was invited, accepted, denied, then granted admin, entirely through the UI
- [ ] All 12 checks recorded

If any check fails, fix forward in the owning step. Do not start Phase 3.

## Commit

```powershell
git add -A
git commit -m "MT-12: Gate E - management app deployed with deploy-time admin bootstrap"
```
