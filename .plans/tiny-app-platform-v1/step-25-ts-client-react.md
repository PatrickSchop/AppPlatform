# Step 25 â€” `@PS/app-client-react`

**Phase:** 5 â€” Front-end
**Depends on:** Step 24
**Working directory:** `C:\Dev\AppPlatform\clients\app-client-react`

## Goal

The React adapter â€” hooks and a provider over `@PS/app-client`. Same thinness rule as
Step 24: under ~300 lines of non-test source.

This package is the actual test of the Â§4 claim. If it needs any backend change, front-end
flexibility was not real. It should need none.

## Tasks

### 1. Package setup

```json
{
  "name": "@PS/app-client-react",
  "version": "0.1.0",
  "type": "module",
  "peerDependencies": {
    "react": ">=18.0.0",
    "@PS/app-client": "^0.1.0"
  }
}
```

Build with `tsup` or plain `tsc` â€” no bundler gymnastics needed for a package this small.
Target React 18+; nothing here requires 19.

### 2. `PlatformProvider`

```tsx
export interface PlatformProviderProps {
  configUrl?: string;
  children: React.ReactNode;
  fallback?: React.ReactNode;      // shown while initialising
  errorFallback?: (error: Error, retry: () => void) => React.ReactNode;
}

export function PlatformProvider(props: PlatformProviderProps): JSX.Element;
```

Calls `createPlatformClient()` once and puts the result on a context. Renders `fallback`
while pending and `errorFallback` on failure.

Two things to get right, because both are easy to get wrong in React:
- **Guard against double-initialisation in StrictMode.** Development double-mounting must not
  produce two `TaskPoller`s â€” one will leak and poll forever.
- **Stop the poller on unmount**, in the effect cleanup.

### 3. Hooks

```ts
export function usePlatform(): PlatformClient;          // throws outside a provider, with a clear message
export function useConfig(): AppConfig;
export function useApi(): ApiClient;
export function useAuth(): AuthClient | null;

export function useBackgroundTasks(): {
  tasks: readonly BackgroundTask[];
  runningTasks: readonly BackgroundTask[];
  hasRunningTasks: boolean;
  refresh: () => Promise<void>;
  createTask: (taskType: string, taskData: unknown, description?: string, requiresNotification?: boolean) => Promise<string>;
};
```

Implement `useBackgroundTasks` with **`useSyncExternalStore`** over the poller's `subscribe`,
not `useState` plus `useEffect`. The poller is an external store; `useSyncExternalStore` is
what it is for, and it gets tearing and concurrent rendering right for free.

`usePlatform` outside a provider must throw a message that names `PlatformProvider` â€” a
generic "cannot read property of null" here costs real debugging time.

### 4. `useApiQuery` â€” a small data hook

```ts
export function useApiQuery<T>(path: string | null, options?: { enabled?: boolean }): {
  data: T | undefined;
  error: Error | undefined;
  loading: boolean;
  refetch: () => Promise<void>;
};
```

A deliberately minimal fetch-on-mount hook: aborts on unmount, refetches when `path` changes,
and does nothing when `path` is null.

**Do not build a cache, deduplication or retry.** An app that needs those should use TanStack
Query with `useApi()` â€” say exactly that in the README, so nobody grows this into a bad
query library.

### 5. `<TaskProgress />`

The unstyled counterpart to Step 24's component: renders running tasks with `className` hooks
and no bundled CSS.

### 6. Tests

`vitest` plus `@testing-library/react`:

1. `PlatformProvider` renders `fallback` then children.
2. `errorFallback` renders on failure and `retry` re-runs initialisation.
3. `useBackgroundTasks` re-renders when the poller emits.
4. `hasRunningTasks` reflects a `Running` task.
5. `usePlatform` outside a provider throws a message naming `PlatformProvider`.
6. **Mounting under `<StrictMode>` creates exactly one poller.**
7. Unmounting stops the poller.
8. `useApiQuery` aborts in flight on unmount and does not set state afterwards.
9. `useApiQuery(null)` never fetches.

Test 6 is the one that matters most; StrictMode double-invocation is the classic React
integration bug and it produces a silent background request leak.

### 7. README

Install, `<PlatformProvider>`, the hooks, and the explicit note that `useApiQuery` is
intentionally minimal and TanStack Query is the answer for anything more.

## Verification

```powershell
cd C:\Dev\AppPlatform\clients\app-client-react
npm install
npm run build
npm test
```

**Expected:** builds under `strict`, all tests pass.

```powershell
(Get-ChildItem src -Recurse -Include *.ts,*.tsx -Exclude *.test.* | Get-Content | Measure-Object -Line).Lines
```

**Expected:** under 300.

## Done when

- [ ] Builds and tests pass
- [ ] `useBackgroundTasks` uses `useSyncExternalStore`
- [ ] StrictMode creates exactly one poller
- [ ] Unmount stops polling
- [ ] `usePlatform` outside a provider throws a message naming the provider
- [ ] Under ~300 lines of non-test source
- [ ] **No backend change was needed to build this package**

## Commit

```powershell
git add -A
git commit -m "Step 25: @PS/app-client-react adapter"
```

