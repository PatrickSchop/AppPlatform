# Step 23 â€” `@PS/app-client`

**Phase:** 5 â€” Front-end
**Depends on:** Step 22
**Working directory:** `C:\Dev\AppPlatform\clients`

**Config contract (added 2026-09-29):** this client reads `auth: { tenantId, clientId, scopes }`
from `/configuration.json`, which passes through the `webApp` configuration key. That section
now ships in both the template and `SampleApp`, so the contract exists before this step starts.

`clientId` there is the **SPA** registration — not the API id held by
`authentication:azureEntraId:clientId` — and `tenantId` is `common` so personal Microsoft
accounts are accepted (§2(4)). Everything under `webApp` is served unauthenticated by design;
both ids are public, and the template ships `clientId` empty for the operator to fill in.

## Goal

Build the zero-dependency TypeScript SDK that every front-end framework then wraps thinly.

## Why this is a small job (analysis Â§4)

The worry was that background tasks would tie the platform to Angular. They do not. The
backend has **zero** Angular coupling, and the task mechanism is plain REST polling, not
SignalR â€” `@microsoft/signalr` has zero imports anywhere in `WebApp/src`, and the hub was
never implemented.

The entire front-end contract is four things: `GET /configuration.json`, `GET /api/tasks/*`,
the catch-all static route, and an `Authorization` header. Nothing framework-specific.

## Reference material (read-only)

`C:\Dev\StockAnalysis\WebApp\src\app\services\background-task.service.ts` â€” ~150 lines. The
adaptive polling logic is the part worth porting; `rxjs` is used only as a `setTimeout`
wrapper and goes away entirely.

## Tasks

### 1. Package setup

`clients/app-client/package.json`:

```json
{
  "name": "@PS/app-client",
  "version": "0.1.0",
  "type": "module",
  "main": "./dist/index.js",
  "types": "./dist/index.d.ts",
  "exports": { ".": { "types": "./dist/index.d.ts", "import": "./dist/index.js" } },
  "files": ["dist"],
  "sideEffects": false,
  "scripts": {
    "build": "tsc -p tsconfig.build.json",
    "test": "vitest run",
    "lint": "tsc --noEmit"
  },
  "peerDependencies": { "@azure/msal-browser": "^3.0.0" },
  "peerDependenciesMeta": { "@azure/msal-browser": { "optional": true } },
  "devDependencies": {
    "typescript": "^5.6.0",
    "vitest": "^2.1.0",
    "@azure/msal-browser": "^3.26.0"
  }
}
```

**`dependencies` must stay empty.** MSAL is an optional peer, so an app that does not need
auth does not pay for it.

`tsconfig.json`: `target: ES2022`, `module: ESNext`, `moduleResolution: Bundler`,
`strict: true`, `declaration: true`, `lib: ["ES2022", "DOM"]`.

### 2. `src/config.ts`

```ts
export interface AppConfig {
  api?: { root?: string };
  auth?: { tenantId?: string; clientId?: string; scopes?: string[] };
  [key: string]: unknown;
}

/** Fetches /configuration.json. Caches the promise so concurrent callers share one request. */
export function loadConfig(url?: string): Promise<AppConfig>;

/** Clears the cache. For tests and for a config reload after sign-in. */
export function resetConfigCache(): void;
```

The index signature matters: apps put their own keys under `webApp` and the client must not
discard them.

### 3. `src/api-client.ts`

```ts
export interface ApiClientOptions {
  baseUrl: string;
  getAccessToken?: () => Promise<string | null>;
  fetch?: typeof fetch;
  onUnauthorized?: () => void;
}

export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;
}

export class ApiClient {
  constructor(options: ApiClientOptions);
  get<T>(path: string, init?: RequestInit): Promise<T>;
  post<T>(path: string, body?: unknown, init?: RequestInit): Promise<T>;
  put<T>(path: string, body?: unknown, init?: RequestInit): Promise<T>;
  delete<T>(path: string, init?: RequestInit): Promise<T>;
}
```

Behaviour:
- `getAccessToken` result, when non-null, becomes `Authorization: Bearer <token>`
- JSON request and response by default; a 204 resolves to `undefined`
- non-2xx throws `ApiError` with the parsed body when it is JSON, the text otherwise
- **401 invokes `onUnauthorized` before throwing** â€” this is what lets an app redirect to
  sign-in from one place
- `fetch` is injectable so tests need no network and no mocking library
- `AbortSignal` passes through via `init`

### 4. `src/task-poller.ts`

The port of the Angular service, framework-free.

```ts
export interface BackgroundTask {
  id: string;
  taskType: string;
  status: 'New' | 'Resumed' | 'NotStarted' | 'Running' | 'Paused' | 'Completed' | 'Failed';
  statusMessage: string;
  completionPercentage: number;
  description: string;
  requiresNotification: boolean;
  createdDate: string;
  updatedDate: string;
  startedDate: string | null;
  completedDate: string | null;
}

export interface TaskPollerOptions {
  api: ApiClient;
  idleIntervalMs?: number;     // default 30000
  activeIntervalMs?: number;   // default 1000
  expectTaskStartMs?: number;  // default 10000
  autoStart?: boolean;         // default true
}

export class TaskPoller extends EventTarget {
  constructor(options: TaskPollerOptions);

  readonly tasks: readonly BackgroundTask[];

  start(): void;
  stop(): void;
  refresh(): Promise<void>;
  /** Poll fast for expectTaskStartMs, for use right after creating a task. */
  expectTaskStart(): void;
  createTask(taskType: string, taskData: unknown, description?: string, requiresNotification?: boolean): Promise<string>;

  subscribe(listener: (tasks: readonly BackgroundTask[]) => void): () => void;
}
```

Port the interval logic exactly: 1s while any task is `Running`, 30s idle, with a 10s
"expect task start" boost after `createTask`. Sort tasks by `createdDate` descending, as the
source does.

`EventTarget` is the DOM-native observable, dispatching a `CustomEvent<readonly BackgroundTask[]>`
named `"tasks"`. `subscribe` is a thin wrapper returning an unsubscribe function â€” that is
what both framework adapters use.

Improve on the source in three ways:
- **Stop polling when the document is hidden** (`document.visibilitychange`), and refresh
  immediately when it becomes visible. The source polls a background tab every second forever.
- **Back off on error**: on consecutive failures, double the interval up to 5 minutes; reset
  on the first success. The source logs and retries at the same rate, which turns a backend
  outage into a request flood.
- **Never overlap requests**: if a poll is in flight, skip the tick.

`createTask` POSTs to `/api/tasks` â€” which exists as of Step 12, closing the analysis Â§7.4
contract drift where the Angular client called an endpoint with no server-side creator.

### 5. `src/auth-client.ts`

```ts
export interface AuthClientOptions {
  tenantId: string;
  clientId: string;
  scopes: string[];
  redirectUri?: string;
}

export class AuthClient {
  static async create(options: AuthClientOptions): Promise<AuthClient>;
  getAccount(): AccountInfo | null;
  isSignedIn(): boolean;
  signIn(): Promise<void>;
  signOut(): Promise<void>;
  getAccessToken(): Promise<string | null>;
}
```

Wraps `@azure/msal-browser`, which is itself framework-agnostic â€” so auth needs no
per-framework work either.

- `import()` MSAL dynamically so it is not in the bundle when unused
- `getAccessToken` tries `acquireTokenSilent`, falling back to `acquireTokenRedirect` on
  `InteractionRequiredAuthError`
- `create` handles the redirect promise before anything else, which is the step people miss
- returns `null` rather than throwing when not signed in, so `ApiClient` can call an
  anonymous endpoint without special-casing

### 6. `src/index.ts`

Re-export everything, plus one convenience:

```ts
/** Loads config, builds an AuthClient when auth is configured, and returns a wired ApiClient and TaskPoller. */
export async function createPlatformClient(options?: { configUrl?: string }): Promise<{
  config: AppConfig;
  auth: AuthClient | null;
  api: ApiClient;
  tasks: TaskPoller;
}>;
```

This is the function both starters call. If it is more than ~30 lines, the pieces are wrong.

### 7. Tests

`vitest`, with an injected `fetch` stub â€” **no network, no jsdom-dependent timing**. Use
`vi.useFakeTimers()`.

1. `loadConfig` fetches once for two concurrent callers.
2. `ApiClient` sets the bearer header when a token is supplied and omits it when null.
3. A 401 calls `onUnauthorized` and then throws `ApiError` with `status === 401`.
4. A 204 resolves to `undefined`.
5. A non-JSON error body is surfaced as text, not a parse failure.
6. `TaskPoller` polls at 30s when idle and 1s once a task is `Running`.
7. `expectTaskStart()` switches to 1s and reverts after 10s.
8. `createTask` POSTs to `/api/tasks` and triggers `expectTaskStart`.
9. Consecutive failures back off and a success resets the interval.
10. A poll is skipped while one is in flight.
11. `subscribe` fires on change and the returned function stops further calls.
12. Hiding the document stops polling; showing it refreshes immediately.

### 8. `clients/app-client/README.md`

Short: install, `createPlatformClient()`, the four-endpoint contract, and a note that this
package has no runtime dependencies.

## Verification

```powershell
cd C:\Dev\AppPlatform\clients\app-client
npm install
npm run build
npm test
```

**Expected:** compiles with `strict`, all tests pass.

Then confirm the zero-dependency claim mechanically:

```powershell
node -e "const p=require('./package.json'); const d=Object.keys(p.dependencies||{}); if(d.length) { console.error('Unexpected dependencies: '+d.join(', ')); process.exit(1); } console.log('OK: no runtime dependencies');"
```

## Done when

- [ ] Builds under `strict`, all tests pass
- [ ] `dependencies` is empty; MSAL is an optional peer
- [ ] `TaskPoller` reproduces the 1s/30s/10s adaptive behaviour
- [ ] It backs off on error, skips overlapping polls and pauses on a hidden tab
- [ ] `createTask` targets `POST /api/tasks`
- [ ] `createPlatformClient` wires everything in one call
- [ ] Nothing imports an Angular, React or rxjs symbol

## Commit

```powershell
git add -A
git commit -m "Step 23: @PS/app-client zero-dependency TypeScript SDK"
```

