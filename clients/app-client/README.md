# @PS/app-client

Zero-dependency TypeScript SDK for the PS App Platform. Provides configuration loading, authenticated API client, and background task polling.

## Installation

```bash
npm install @PS/app-client @azure/msal-browser
```

Note: `@azure/msal-browser` is an optional peer dependency. Include it if you need authentication, or omit it for public APIs.

## Usage

### Basic setup

```typescript
import { createPlatformClient } from '@PS/app-client';

const { config, auth, api, tasks } = await createPlatformClient();

// Check if authenticated
if (auth?.isSignedIn()) {
  console.log('Signed in as', auth.getAccount()?.name);
}

// Make an API call
const data = await api.get('/api/data');

// Monitor background tasks
tasks.subscribe((tasks) => {
  console.log('Tasks:', tasks);
});
```

### Configuration contract

The backend serves `/configuration.json` with this shape:

```json
{
  "api": { "root": "https://api.example.com" },
  "auth": {
    "tenantId": "common",
    "clientId": "your-spa-client-id",
    "scopes": ["api://api-id/access"]
  },
  "webApp": { /* custom keys for your app */ }
}
```

- Everything under `webApp` is served unauthenticated and custom to your application.
- `clientId` is the **SPA** registration, not the API id.
- Both `tenantId` and `clientId` are public; nothing secret should be under `webApp`.

### API endpoints

The platform expects four endpoints (all but the first require authentication):

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/configuration.json` | Configuration (public) |
| `GET` | `/api/tasks` | List background tasks |
| `POST` | `/api/tasks` | Create a background task |
| `GET` | `/api/tasks/{id}` | Get task details |

### Imports

All exports are framework-agnostic:

```typescript
import {
  loadConfig,          // (url?: string) => Promise<AppConfig>
  resetConfigCache,    // () => void
  ApiClient,           // HTTP client with auth
  ApiError,            // Error subclass with status and body
  TaskPoller,          // Background task polling with adaptive intervals
  AuthClient,          // MSAL wrapper
  createPlatformClient, // Convenience function to wire everything
} from '@PS/app-client';
```

## Runtime dependencies

**Zero.** All dependencies are dev-only (`typescript`, `vitest`, `@azure/msal-browser` for testing).

The `@azure/msal-browser` module is imported dynamically only when `AuthClient.create()` is called, keeping it out of bundles when auth is not needed.

## Features

- **Zero external dependencies** at runtime
- **Adaptive polling**: 1s while tasks run, 30s when idle, 10s boost after creation
- **Error backoff**: doubles interval on consecutive failures (up to 5 minutes)
- **Visibility aware**: stops polling when tab is hidden, refreshes immediately when shown
- **Non-overlapping requests**: skips poll ticks while one is in flight
- **Optional authentication**: MSAL integration with dynamic import
- **EventTarget observable**: built on DOM's native event system, subscribe with plain functions
