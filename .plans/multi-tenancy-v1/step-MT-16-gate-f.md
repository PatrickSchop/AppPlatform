# Step MT-16 — Gate F: a multi-tenant app from the template, end to end

**Phase:** 3 — Front-end and template (final gate)
**Depends on:** MT-15
**Working directory:** a new repository, `PatrickSchop/TenantScratch` (like v1's ScratchApp)

## Goal

The multi-tenancy counterpart of v1 Gate B: from `dotnet new` to a deployed multi-tenant app
with tenant selection, registered roles and isolated data, **consuming only published
packages**, with nothing copied from `MultiTenantSample` or the management app.

## Tasks

### 1. Scaffold and extend

```powershell
dotnet new install PS.AppPlatform.Templates::0.2.0
dotnet new tinyapp -n TenantScratch --Tenancy multi --Frontend angular --AppName tenantscratch
```

Then, by hand, as an app author would (and time it):
- add one more `TenantEntity` (`Note`) with its script, endpoint and shim;
- add one application-wide entity (`Category`);
- add one anonymous endpoint `POST /api/public/{slug}/notes` that resolves a tenant from an
  app-owned alias table and writes with `CreateForTenant`;
- add a role `reporter` to the manifest and a cross-tenant report endpoint on
  `IUnscopedDbContextFactory` guarded by `[Authorize(Roles="reporter")]`.

### 2. Deploy

- `infra/app.bicep` with `appName=tenantscratch`, `managementUrl`, `managementAudience`.
- Add the deploy principal's oid to the management app's `registry__trustedDeployers`, if it
  is a different principal from Gate E's.
- Add the app origin to the PS Apps SPA redirect URIs.
- Push; the workflow migrates, **registers**, deploys.

## Gate F checklist

P = operator (management admin), Q = second identity (from Gate E).

| # | Action | Expected |
|---|---|---|
| 1 | Workflow log | `--register`: `Created: true`, roles `editor`, `viewer`, `reporter` |
| 2 | Management UI | `tenantscratch` listed with `Default` tenant + team and three roles |
| 3 | P creates tenants "Alpha" and "Beta"; adds Q as `editor` in Alpha and `viewer` in Beta | done in UI only |
| 4 | Q opens the app, signs in | tenant picker with Alpha and Beta |
| 5 | Q picks Alpha, creates a note | 200; note listed |
| 6 | Q switches to Beta | Alpha's note absent; create is refused (viewer) |
| 7 | Q reloads | Beta still selected; no picker |
| 8 | Anonymous `POST /api/public/alpha/notes` | stored in Alpha only (check in the UI as Q) |
| 9 | Q calls the report endpoint | 403; P grants `reporter` in Alpha; within ~5 min Q gets counts for **both** tenants |
| 10 | P removes Q from Beta | within ~5 min Q's switcher shows Alpha only; a stored Beta choice falls back cleanly |
| 11 | P disables Q | Q gets "not registered" within the cache window |
| 12 | Remove `reporter` from the manifest and redeploy | registration reports it `Deprecated`; Q's assignment is kept but grants nothing; the management UI flags it |
| 13 | `dotnet new tinyapp --Tenancy single` built and deployed the same way (short run) | no picker; the single tenant is resolved automatically |
| 14 | Stop the management Function App (`az functionapp stop`), use TenantScratch | cached users keep working (stale-if-error); a user not in cache gets `registry_unavailable`; restart the management app |
| 15 | First request after both apps have idled 20+ min | completes. Record the latency; it is the D1 cold-start cost, not a pass/fail criterion |

Record the elapsed time for task 1 (target: under an hour, as v1 Gate B).

## Verification

The checklist, recorded with date and timings in `multi-tenancy-v1-progress.md`.

## Done when

- [ ] All 15 checks recorded; 1–14 pass
- [ ] Nothing was copied from `MultiTenantSample` or the management app
- [ ] The container plan's requirement traceability table holds: every row was exercised here
      or at Gate D/E

## Commit

In this repository, only the progress update:

```powershell
git add .plans/multi-tenancy-v1-progress.md
git commit -m "MT-16: Gate F - multi-tenant app from the template, deployed and verified"
```
