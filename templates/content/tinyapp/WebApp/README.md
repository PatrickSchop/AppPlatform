# Angular starter

A front-end for apps on the PS App Platform: sign-in with Microsoft Entra, optional tenant selection,
a background-task indicator and an example page that calls the API. It follows the same patterns and
look as the management app (`apps/Management/WebApp`): semantic colour tokens in `src/styles.scss`,
a top bar with a sidebar, standalone components, and the platform adapter for everything that talks
to the backend.

## Getting started

```bash
npm install
npm start          # http://localhost:4200
```

Run the backend next to it (`func start` in your app, or `samples/SampleApp`). `/api` is proxied to
`http://localhost:7071` by `proxy.conf.json`, so there are no CORS settings to maintain.

`npm run build` writes the app to `dist/WebApp/browser`; `npm test` runs the unit tests.

## How configuration works

The platform client reads `/configuration.json` once at startup (`provideAppPlatform()`): the API
root, the sign-in settings, the title and the tenancy mode all come from it.

- **Deployed**: the backend serves `/configuration.json` from its `webApp` settings. Nothing in the
  front-end build carries configuration, so the same build runs in every environment. The backend's
  route wins over any file of that name in the build output.
- **Development**: `public/configuration.json` is served by `ng serve`, so the app starts without a
  backend. It has no sign-in settings, so the app runs signed out and calls to protected endpoints
  fail with 401. To develop with sign-in and tenancy, copy the real configuration over it:

  ```bash
  curl http://localhost:7071/configuration.json -o public/configuration.json
  ```

  Do not commit that copy: it holds your app registration ids.

This is confusing the first time: the file in `public/` is for `ng serve` only, and is not what a
deployed app reads.

## What is in the box

| | |
|---|---|
| `app.config.ts` | The providers: router, HTTP with the token interceptor, the platform client, tenancy |
| `app.routes.ts` | `/` (public), `/example` (signed-in, tenant selected), `/forbidden`, `/error`, and the tenant pages |
| `guards/auth.guard.ts` | Sends a signed-out user to sign in; passes everyone when authentication is not configured |
| `pages/forbidden` | Tells apart "not signed in", "sign-in failed" and "signed in but not allowed" (the latter says to ask the administrator) |
| `components/task-status` | The top-bar indicator that expands to per-task progress bars |

Authentication needs no App Role by default. If your app sets `requiredRole`, users without it get a
403 from the API; the example page sends them to `/forbidden`.

Tenancy is decided by the backend's configuration. With tenancy off, the tenant guard and switcher do
nothing. With `Single` or `Multi`, the picker (`/select-tenant`) appears when a user has several tenants and the
switcher appears in the top bar.

## Add a page

1. Create a standalone component under `src/app/pages/`.
2. Add a route under the guarded group in `app.routes.ts` (the one with `authGuard` and `tenantGuard`).
3. Add a link in `app.component.html`.

## Call the API

Inject `API_CLIENT`; it adds the access token and the selected tenant to every request:

```ts
private api = inject(API_CLIENT) as unknown as ApiClient;
getItems(): Promise<Item[]> { return this.api.get('items'); }
```

See `services/example.service.ts` and `pages/example`. Errors are `ApiError`s with `status` and
`body`; `shared/error-messages.ts` turns them into text for a toast.

## Start a background task

```ts
private tasks = inject(BackgroundTaskService);
await this.tasks.createTask('wordcount', {}, 'Counting words', true);
```

The task type must have a handler in the backend. Progress appears in the top bar by itself;
`BackgroundTaskService` polls and exposes `tasks()`, `runningTasks()` and `hasRunningTasks()` signals.

## In the `dotnet new tinyapp` template

`dotnet new tinyapp --frontend angular` puts this app in `WebApp/`. The copy under
`templates/content/tinyapp/WebApp` is generated: edit the starter, then run
`starters/sync-to-template.sh`.
