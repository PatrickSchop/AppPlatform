# @PS/app-client-react

React 18+ adapter for the PS App Platform. Provides hooks, a provider, and components for accessing platform services.

## Installation

```bash
npm install @PS/app-client @PS/app-client-react
```

## Usage

Wrap your app with `<PlatformProvider>`:

```tsx
import { PlatformProvider } from '@PS/app-client-react';

function App() {
  return (
    <PlatformProvider
      fallback={<div>Loading platform...</div>}
      errorFallback={(error, retry) => (
        <div>
          Failed to load: {error.message}
          <button onClick={retry}>Retry</button>
        </div>
      )}
    >
      <YourAppContent />
    </PlatformProvider>
  );
}
```

## Hooks

### usePlatform

Access the entire platform client:

```tsx
const platform = usePlatform();
// platform.config, platform.api, platform.auth, platform.tasks
```

### useConfig

Get the loaded configuration:

```tsx
const config = useConfig();
const apiRoot = config.api?.root;
```

### useApi

Get the authenticated API client:

```tsx
const api = useApi();
const data = await api.get('/api/data');
```

### useAuth

Get the auth client (null if auth not configured):

```tsx
const auth = useAuth();
if (auth?.isSignedIn()) {
  // ...
}
```

### useBackgroundTasks

Monitor and manage background tasks. Uses `useSyncExternalStore` for proper integration with the polling system:

```tsx
function TasksDisplay() {
  const { tasks, runningTasks, hasRunningTasks, createTask, refresh } = useBackgroundTasks();

  return (
    <div>
      {hasRunningTasks && <div>Working...</div>}
      {tasks.map(t => (
        <div key={t.id}>{t.taskType}: {t.status}</div>
      ))}
    </div>
  );
}
```

### useApiQuery

A minimal data-fetching hook. **Intentionally does not include caching, deduplication or retry.** For anything beyond simple fetch-on-mount, use [TanStack Query](https://tanstack.com/query/latest) with `useApi()`:

```tsx
const { data, loading, error, refetch } = useApiQuery<DataType>('/api/data', {
  enabled: someCondition,
});

if (loading) return <div>Loading...</div>;
if (error) return <div>Error: {error.message}</div>;
return <div>{data}</div>;
```

Features:
- Aborts in-flight requests on unmount
- Refetches when path changes
- Does nothing when path is null
- No cache, deduplication or retry logic

## Components

### TaskProgress

Unstyled task progress display:

```tsx
<TaskProgress />
```

Style with CSS classes:
- `.ps-task-progress` — container
- `.ps-task-item` — individual task
- `.ps-task-header` — title area
- `.ps-task-type` — task type label
- `.ps-task-status` — status label
- `.ps-task-description` — task description
- `.ps-task-progress-bar` — progress bar container
- `.ps-task-progress-fill` — filled progress bar
- `.ps-task-percentage` — percentage text

## Key Details

- **StrictMode safe**: The provider guards against double-initialization in React 18+ strict mode
- **Proper cleanup**: The poller is stopped on unmount to prevent background polling
- **useSyncExternalStore**: Background tasks use React's external store hook for proper integration with concurrent rendering
- **Zero dependencies** at runtime (peers only: React and @PS/app-client)
