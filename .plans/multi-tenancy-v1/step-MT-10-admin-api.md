# Step MT-10 — Admin API and invitations

**Phase:** 2 — Management app
**Depends on:** MT-09
**Working directory:** `C:\Dev\AppPlatform\apps\Management`

## Goal

Every management task the UI (MT-11) needs, as an HTTP API: applications, tenants, teams,
users, memberships, role assignments and invitations. Also the one-time invite flow that binds
a registered user to a Microsoft identity (D5).

## Conventions for every endpoint in this step

- Admin-only through the app's default policy (`requiredRole: admin`, MT-08). No attribute is
  needed, and none may weaken it, **except** the two invitation endpoints under "Accepting an
  invitation".
- Endpoint classes in `Api/Admin*Endpoints.cs` with shim classes in `Api/Functions/`, following
  the `SampleApp` endpoint + shim pattern. Business rules live in `RegistryService` (MT-08),
  not in endpoints.
- Request/response records in `Api/Contracts/`, camelCase JSON. Never return EF entities.
  Never return `Invitation.TokenHash`.
- Errors: 400 `{ error: "validation", details: { field: message } }`; 404 for unknown ids;
  409 `{ error: "<code>" }` for rule violations (codes below).
- Every mutation logs actor oid, action and target ids at Information level. That is the audit
  trail until a real one exists (out of scope).

## Routes

### Applications

| Method | Route | Notes |
|---|---|---|
| GET | `/api/admin/applications` | key, displayName, tenancy, tenant count, user count, isDisabled |
| GET | `/api/admin/applications/{appId}` | + roles (with `isDeprecated`, assignment count) and tenants |
| PATCH | `/api/admin/applications/{appId}` | `displayName`, `isDisabled` only. Roles come from code (D2) |
| DELETE | `/api/admin/applications/{appId}/roles/{roleId}` | only a **deprecated** role with **zero** assignments, else 409 `role_in_use` / `role_not_deprecated` |

The `management` application cannot be disabled: 409 `cannot_disable_management`.

### Tenants and teams

| Method | Route | Notes |
|---|---|---|
| GET/POST | `/api/admin/applications/{appId}/tenants` | POST `{ name }` creates the tenant **and its `Default` team**. `Single` app with a tenant already → 409 `single_tenant_app` |
| PATCH | `/api/admin/tenants/{tenantId}` | `name`, `isDisabled` |
| DELETE | `/api/admin/tenants/{tenantId}` | refuses the application's last tenant: 409 `last_tenant` |
| GET/POST | `/api/admin/tenants/{tenantId}/teams` | POST `{ name }` |
| PATCH/DELETE | `/api/admin/teams/{teamId}` | the `Default` team cannot be renamed or deleted: 409 `default_team` |

Deleting a tenant or team is a hard delete that cascades (MT-08 keys). The UI must confirm it;
the API does not second-guess.

### Users, memberships, roles

| Method | Route | Notes |
|---|---|---|
| GET | `/api/admin/users?search=&skip=&take=` | displayName/email contains; `status` = `invited` \| `active` \| `disabled` |
| POST | `/api/admin/users` | `{ displayName, email? }` → unbound user (status `invited`) |
| GET | `/api/admin/users/{userId}` | + memberships across all applications: app, tenant, team, roles |
| PATCH | `/api/admin/users/{userId}` | `displayName`, `email`, `isDisabled` |
| GET/POST | `/api/admin/teams/{teamId}/members` | POST `{ userId, roleIds[] }` |
| PUT | `/api/admin/members/{memberId}/roles` | `{ roleIds[] }`, replaces the set |
| DELETE | `/api/admin/members/{memberId}` | |

Rules in `RegistryService`:
- every `roleId` must belong to the **same application** as the team's tenant; else 400;
- deprecated roles cannot be newly assigned; else 400;
- **last-admin guard:** any change that would leave `management` with zero enabled, bound users
  holding `admin` (removing the role, the membership, disabling or deleting the user) → 409
  `last_admin`.

### Invitations

| Method | Route | Notes |
|---|---|---|
| POST | `/api/admin/users/{userId}/invitations` | only for unbound users (else 409 `already_bound`). Revokes earlier open invitations. Returns `{ invitationId, url, expiresUtc }`. **The only time the token is ever returned** |
| GET | `/api/admin/users/{userId}/invitations` | status list, no tokens |
| DELETE | `/api/admin/invitations/{invitationId}` | revoke |

Token: 32 bytes from `RandomNumberGenerator`, base64url; store the lowercase hex SHA-256 in
`TokenHash`. `url` = `{registry:publicBaseUrl}/invite/{token}`. Expiry `registry:invitationLifetime`,
default 7 days.

### Accepting an invitation (not admin-only)

Both carry `[Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]` and `[TenantOptional]`,
because the invitee is authenticated but, by definition, not yet registered. Add a review
comment saying why (v1 §4.1a).

| Method | Route | Response |
|---|---|---|
| GET | `/api/invitations/{token}` | `{ displayName, applications: [names], expiresUtc }` if valid; 404 if unknown/revoked; 410 `invitation_expired` / `invitation_used` |
| POST | `/api/invitations/{token}/accept` | binds caller `oid`/`tid` to the user, sets `BoundUtc`, `AcceptedUtc`; 200 `{ displayName }` |

Accept rules, all in one transaction:
- same 404/410 cases as GET;
- the caller's (`oid`, `tid`) is already bound to **another** user → 409
  `identity_already_registered` (the unique index would reject it anyway; map that to the code
  rather than a 500);
- the target user is already bound → 409 `already_bound`;
- look tokens up by hash with a constant-time comparison of the stored hash;
- after accepting, remove the caller's `(oid, tid)` from the local directory cache, so the new
  admin (if any) is effective immediately in the management app. Other apps pick the user up
  within the null-cache period (MT-09: at most 1 minute).

## Tests to add (`tests/Management.Tests`)

Service level (InMemory):
1. Creating a tenant creates its `Default` team; `Single` app refuses a second tenant.
2. Cross-application role assignment → 400; deprecated role → 400.
3. Last-admin guard on each of: remove role, remove membership, disable user.
4. Default team cannot be renamed or deleted; last tenant cannot be deleted.
5. Invitation: create returns a token whose hash is stored; a second create revokes the first;
   accept binds; accept again → `invitation_used`; expired → `invitation_expired`; identity
   already bound elsewhere → `identity_already_registered`.

Pipeline level (MT-04 `Invoke` harness):
6. A non-admin registered user → 403 on `GET /api/admin/applications`.
7. An unregistered authenticated user → 200 on `GET /api/invitations/{token}` and can accept;
   → 403 on every `/api/admin/*` route.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

Local walkthrough against `func start` (as the bootstrapped admin, token as in Gate D):
create an application tenant for `multitenantsample` (registered in MT-09) → create a user →
add them to the tenant's `Default` team with `editor` → create an invitation → `GET` the
invitation → verify the response carries no hash. Accepting needs a second identity; that is
done for real at Gate E.

## Done when

- [ ] Every route above exists and is covered by a service test or the pipeline tests
- [ ] Only the two invitation endpoints are reachable by non-admins
- [ ] Tokens are single-use, expiring, hashed at rest and returned exactly once
- [ ] The management app cannot lose its last admin through the API

## Commit

```powershell
git add -A
git commit -m "MT-10: admin API for the registry and one-time invitations"
```
