# Multi-tenancy v1 — Progress

**Updated:** 2026-09-29 · **Phase 1 progressing · next: MT-04**

Plan: [multi-tenancy-v1.md](multi-tenancy-v1.md) · Steps: [multi-tenancy-v1/](multi-tenancy-v1/)

## Status at a glance

| | Steps | State |
|---|---|---|
| Phase 0 — Prerequisites | MT-01 | ✅ complete |
| Phase 1 — Core tenancy | MT-02 – MT-07 | 🚧 2 of 6 complete · **Gate D** outstanding |
| Phase 2 — Management app | MT-08 – MT-12 | ⏳ not started · **Gate E** outstanding |
| Phase 3 — Front-end and template | MT-13 – MT-16 | ⏳ not started · **Gate F** outstanding |

**3 of 16 steps complete.**

## Dependencies on the v1 plan

| This plan | Needs from v1 | v1 state (2026-09-29) |
|---|---|---|
| MT-01 – MT-10 | Phases 0–4 (platform `0.1.2`) | ✅ available |
| MT-11 (management UI) | Step 26 — Angular starter, `@PS/app-client-angular` | ⏳ not started |
| MT-13 onward | Step 28 — Gate C (clients and both starters) | ⏳ not started |

Phases 0–1 and MT-08 – MT-10 can run in parallel with v1 Phase 5. MT-11 waits for v1 Step 26.

## Steps

| Step | Outcome | State | Tests | Commit |
|---|---|---|---|---|
| MT-01 | `PlatformCommandLine` | ✅ | ✅ | ✅ |
| MT-02 | Tenancy contracts, config directory | ✅ | ✅ | ✅ |
| MT-03 | `TenantEntity`, filters, factories | ✅ | ✅ | ✅ |
| MT-04 | Tenant resolution, registry roles | ⏳ | | |
| MT-05 | Tenant-aware background tasks | ⏳ | | |
| MT-06 | `/api/me/tenants`, conditional shims | ⏳ | | |
| MT-07 | **Gate D** — `MultiTenantSample` | ⏳ | | |
| MT-08 | Management backend, bootstrap | ⏳ | | |
| MT-09 | Registry API, `--register` | ⏳ | | |
| MT-10 | Admin API, invitations | ⏳ | | |
| MT-11 | Management UI | ⏳ blocked on v1 Step 26 | | |
| MT-12 | **Gate E** — management deployed | ⏳ | | |
| MT-13 | `TenantSession` | ⏳ blocked on v1 Step 28 | | |
| MT-14 | Angular/React tenancy components | ⏳ | | |
| MT-15 | Template `--Tenancy`, deploy registration, `0.2.0` | ⏳ | | |
| MT-16 | **Gate F** — template app end to end | ⏳ | | |

## Gates

**Gate D (MT-07) — outstanding.** 18 checks; results go here.

**Gate E (MT-12) — outstanding.** 12 checks; results go here.

**Gate F (MT-16) — outstanding.** 15 checks and the scaffold-to-deploy time; results go here.

## Decisions taken while planning

| Decision | Choice | Where |
|---|---|---|
| Membership lookup | Management API + per-app cache (5 min; 1 min for "not registered"; stale-if-error 1 h) | D1, MT-09 |
| Registration timing | At deployment (`--register`), not at startup | D2, MT-15 |
| Binding users to identities | One-time invite link; email is never matched | D5, MT-10 |
| Tenant resolution placement | A service inside `FunctionAuthorizationMiddleware`, not a separate middleware | D3, MT-04 |
| Token roles in registry apps | Dropped; the registry is the only source of roles | D3, MT-04 |
| Bootstrap admin ids | Repository variables, not secrets | D5, MT-12 |

## Operator actions (when their steps arrive)

- MT-07: note your `oid`/`tid` from a decoded token.
- MT-12: create the `Management` resource group and deploy; set app settings; add the SPA
  redirect URI; set the `BOOTSTRAP_ADMIN_*` repository variables; grant the deploy principal
  `db_owner` on the `management` database; have a second Microsoft account ready.
- MT-16: create `PatrickSchop/TenantScratch`; add its origin to the SPA redirect URIs.

## Next

Start **MT-01** — `PlatformCommandLine`.
