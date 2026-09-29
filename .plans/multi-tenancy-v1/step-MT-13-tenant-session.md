# Step MT-13 — `TenantSession` in `@PS/app-client`

**Phase:** 3 — Front-end and template
**Depends on:** MT-12 (Gate E), **and v1 Step 28** (Gate C: the clients and both starters exist)
**Working directory:** `C:\Dev\AppPlatform\clients\app-client`

## Goal

The framework-free half of the standard login flow (D6): after sign-in, find the user's
tenants, select one automatically or ask, remember the choice, send it on every request, and
recover when the server rejects it. The Angular and React adapters (MT-14) only render this
state.

It stays within v1 Step 23's rules: **zero runtime dependencies**, `EventTarget` for change
notification, injected `fetch` and storage for tests.

## Tasks

### 1. `src/tenant-session.ts`

```ts
export interface TenantInfo { tenantId: string; name: string; roles: string[]; }

export interface MyTenantsResponse {        // GET /api/me/tenants (MT-06)
  registered: boolean;
  displayName: string | null;
  tenants: TenantInfo[];
}

export type TenantState =
  | { status: 'idle' }                                  // not signed in / not loaded
  | { status: 'loading' }
  | { status: 'not-registered'; displayName: string | null }
  | { status: 'selecting'; tenants: TenantInfo[] }
  | { status: 'selected'; tenant: TenantInfo; tenants: TenantInfo[] }
  | { status: 'error'; error: unknown };

export interface TenantSessionOptions {
  api: ApiClient;
  header: string;                 // from configuration.json tenancy.header
  storageKey: string;             // see task 3
  storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>; // default: localStorage if available
}

export class TenantSession extends EventTarget {
  constructor(options: TenantSessionOptions);
  readonly state: TenantState;
  readonly currentTenantId: string | null;
  load(): Promise<TenantState>;         // idempotent while loading
  select(tenantId: string): void;       // throws if not in tenants
  reset(): void;                        // forget the stored choice → selecting (or auto)
  hasRole(role: string): boolean;       // in the selected tenant
  headers(): Record<string, string>;    // {} or { [header]: currentTenantId }
  subscribe(listener: (s: TenantState) => void): () => void;
}
```

`load()` rules:
- `registered === false` or no tenants → `not-registered`;
- exactly one tenant → `selected` (store it);
- stored id present **and** still in the list → `selected`;
- otherwise → `selecting` (a stale stored id is removed).

`select()` stores the id, moves to `selected`, and dispatches `change`.

### 2. `ApiClient` changes (`src/api-client.ts`)

Extend `ApiClientOptions`. These are additive, so existing callers are unaffected:

```ts
getHeaders?: () => Record<string, string>;           // merged into every request
onErrorCode?: (status: number, code: string) => void; // body.error of a non-2xx JSON response
```

`onErrorCode` fires before `ApiError` is thrown, in the same position as `onUnauthorized`
(v1 Step 23). The request that got the error still throws. Recovery happens in the UI.

### 3. Wiring in `createPlatformClient`

When `config.tenancy?.mode` is `Single` or `Multi`:
- build `TenantSession` with `header = config.tenancy.header` and
  `storageKey = "ps-tenant:" + location.origin + ":" + (auth.getAccount()?.homeAccountId ?? "anon")`;
  that is per app and per account, so switching accounts never reuses another account's tenant;
- pass `getHeaders: () => tenant.headers()` to the `ApiClient`;
- `onErrorCode`: `tenant_required` / `tenant_forbidden` → `tenant.reset()`; `not_registered` →
  reload the session (which lands in `not-registered`);
- on `change` → `tasks.refresh()`, because task lists are per tenant (MT-05);
- return `{ config, auth, api, tasks, tenant }`, with `tenant: TenantSession | null` (null in
  `None` mode).

The `TaskPoller` must **not start polling** until the tenant state is `selected`, or it would
poll into 409s. Start it on the first `selected` state.

### 4. `AppConfig`

Add `tenancy?: { mode: 'None' | 'Single' | 'Multi'; header: string }` to the type (v1 Step 23
kept an index signature, so this is typing only).

## Tests to add (vitest, fake `fetch`, fake storage; no jsdom timing)

1. One tenant → `selected` without user input, and it is stored.
2. Two tenants, nothing stored → `selecting`; `select(B)` → `selected`, stored, `change` fired.
3. Stored B still valid → `selected` B on load; stored id no longer in the list → `selecting`
   and the key is removed.
4. `registered: false` → `not-registered`.
5. `headers()` is `{}` before selection and `{ 'X-Tenant-Id': B }` after.
6. `ApiClient` merges `getHeaders()` into requests and calls `onErrorCode` with `tenant_forbidden`
   before throwing.
7. `createPlatformClient` in `Multi` mode: a 409 `tenant_required` resets the session to
   `selecting`; `TaskPoller` makes no request until `selected`; a tenant change triggers a task
   refresh.
8. `None` mode: `tenant` is null, no `/api/me/tenants` request is made, and requests carry no
   tenant header.
9. Storage keys differ per account.
10. `dependencies` in `package.json` is still empty (the v1 Step 23 check).

## Verification

```powershell
cd C:\Dev\AppPlatform\clients\app-client
npm run build
npm test
node -e "const p=require('./package.json'); if(Object.keys(p.dependencies||{}).length){process.exit(1)} console.log('OK: no runtime dependencies')"
```

**Expected:** strict build, all tests pass (old and new), no runtime dependencies. Bump the
package minor version.

## Done when

- [ ] The full login-flow state machine lives here, with no framework code
- [ ] Rejected tenants recover to the picker automatically
- [ ] `None`-mode apps see no behavioural change

## Commit

```powershell
git add -A
git commit -m "MT-13: TenantSession and tenant-aware ApiClient in @PS/app-client"
```
