# Step MT-11 — Management Angular UI

**Phase:** 2 — Management app
**Depends on:** MT-10 and the packages `@PS/app-client` / `@PS/app-client-angular` (v1 Steps 23–24, already done). **Not** blocked on v1 Step 26.
**Working directory:** `C:\Dev\AppPlatform\apps\Management\WebApp`

## Goal

A complete front-end for every MT-10 route, so no management task needs SQL, Postman or the
Azure portal. It is built **from scratch** as its own Angular app on the published client
packages, so it shares the API client and sign-in with every other app without depending on a
starter.

**Why no starter:** the v1 Angular starter (Step 26) should demonstrate the finished
tenant-aware login flow, which this plan delivers (MT-13/MT-14). Scaffolding the management
UI from it would be circular. This UI is built first; the starter later borrows from it.
Do not wait for Step 26, and do not copy `starters/angular`.

## Tasks

### 1. Scaffold from scratch

Create `apps/Management/WebApp` with `ng new` (standalone components, strict templates,
routing, SCSS). Install `@PS/app-client` and `@PS/app-client-angular` and wire
`provideAppClient`/sign-in per the package README and v1 §4.1a. Build a small shell of your own:
top bar with the signed-in account and sign-out, a side nav, a shared confirm dialog, a toast/
banner component, and 401/403 handling via the client's error hooks. Keep styling minimal and
consistent (CSS variables, no UI framework dependency beyond what Angular ships, unless one is
clearly worth it). Do not add background-task UI; this app does not use it.

`configuration.json` for local dev comes from the management app's `webApp` section
(`api.root`, `title: "Application management"`, `auth` as in v1 §4.1a). `tenancy` is added
automatically (MT-06, mode `Single`). The app has no tenant picker (Single mode); the
`TenantSession`/picker work in MT-13/MT-14 is not a prerequisite, and the admin gate below
calls `GET /api/me/tenants` directly.

### 2. Admin gate

On startup, after sign-in, call `GET /api/me/tenants` directly. The management app is
`Single`, so it needs no picker (that arrives with MT-14 and is not needed here):
- `registered && roles` include `admin` → the app;
- anything else → a "No access" page that shows the signed-in account (name, and oid/tid in a
  copyable block) and says: "Ask an administrator to invite you." Showing the ids lets the
  operator bootstrap or debug without decoding tokens.

The API remains the security boundary (v1 §4.1a). This gate is UX only.

### 3. Pages and routes

| Route | Page | Uses |
|---|---|---|
| `/applications` | list: key, name, tenancy badge, tenants, users, disabled | `GET applications` |
| `/applications/:appId` | detail. Roles tab (deprecated flagged; delete only when allowed). Tenants tab (create, rename, disable, delete). Settings (display name, disable) | applications, tenants |
| `/tenants/:tenantId` | teams list (create, rename, delete; `Default` locked) | tenants/teams |
| `/teams/:teamId` | members table with role checkboxes per member; add member (user search typeahead + roles) | members, roles |
| `/users` | search, status filter (`invited`/`active`/`disabled`), create user | users |
| `/users/:userId` | profile edit, disable, memberships across apps (links to teams), invitations list, **Create invite link** | users, invitations |
| `/invite/:token` | public-facing accept page (task 4) | invitations |

Breadcrumbs: Application › Tenant › Team. For `Single` applications, hide "create tenant"
(the API refuses it anyway) and label the tenant "Default".

### 4. Invite flow in the UI

- **Admin side:** "Create invite link" shows the URL **once** in a dialog with a copy button,
  the expiry, and the warning "This link will not be shown again. Anyone who opens it first
  can claim this user." Creating a new link revokes the old one; say so in the dialog.
- **Invitee side (`/invite/:token`):** this route must not require admin. If the visitor is
  not signed in, sign in with `redirectStartPage` pointing back here. Then `GET` the
  invitation: show who it is for and the applications involved, with an **Accept** button →
  `POST .../accept` → a success page. Map `invitation_expired`, `invitation_used`,
  `identity_already_registered` and `already_bound` to plain-language messages.
- After a successful accept, a user who received `admin` is taken to `/applications`; everyone
  else sees "You now have access to: …" with the applications' names.

### 5. Error handling and UX rules

- 409 codes from MT-10 map to specific messages (`last_admin`: "This would leave no
  administrator").
- Destructive actions (delete tenant, team, member, role) require a confirm dialog that names
  what will be deleted, including the cascade ("and its 3 teams and 12 memberships").
- A banner after any membership or role change: "Changes can take up to 5 minutes to reach
  running apps." This is the D1 cache.
- Lists are paged server-side for users; everything else is small enough to load whole.

### 6. Build and hosting

`package.json` build output to `dist/WebApp/browser` (the `frontend_dist_path` convention in
`app-build.yaml`). Local dev: `ng serve` against `func start` with the CORS origin
`http://localhost:4200` in the management app's development settings.

## Tests to add

- Component tests for: the admin gate (admin / non-admin / unregistered), the invite dialog
  (token shown once; closing clears it), the accept page (each error code → message), and the
  role checkbox grid (sends the full set to `PUT .../roles`).
- `npm run build` with `strict` templates, zero warnings.

## Verification

```powershell
cd C:\Dev\AppPlatform\apps\Management\WebApp
npm ci
npm test
npm run build
```

Then, running locally (`func start` in `apps\Management` + `ng serve`), as the bootstrapped
admin:
1. `/applications` lists `management` and `multitenantsample`.
2. Create tenant "Fabrikam" on `multitenantsample`; it shows a `Default` team.
3. Create user "Test user", add them to Fabrikam/Default with `viewer`, create an invite link.
4. Try to remove your own `admin` role → the `last_admin` message.

**Expected:** each action succeeds or fails with the documented message, with no console
errors. The real second-identity accept is Gate E.

## Done when

- [ ] The app is its own `ng new` project: nothing copied from `starters/angular`
- [ ] Every MT-10 capability is reachable from the UI
- [ ] A non-admin sees "No access" with their oid/tid, never a broken page
- [ ] The invite link is shown exactly once and the accept page handles every error code
- [ ] Build and tests green

## Commit

```powershell
git add -A
git commit -m "MT-11: management Angular UI"
```
