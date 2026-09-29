# Step 26 â€” Angular starter

**Phase:** 5 â€” Front-end
**Depends on:** Step 25
**Working directory:** `C:\Dev\AppPlatform\starters\angular`

**Amended 2026-09-29 (container plan §10 A1):** these apps require authentication but **no
App Role**, so a 403 cannot occur in the default configuration. Keep the 403 branch — the
platform still supports `requiredRole` and an app may set it — but do not make holding a
role a condition of signing in, and do not treat the 403 page as reachable in a normal run.


## Goal

A ready-to-use Angular front-end: the existing `WebApp` **minus the stock domain**, keeping
its design system, wired to `@PS/app-client-angular`, and with the MSAL sign-in the source
app never had.

## Reference material (read-only)

`C:\Dev\StockAnalysis\WebApp\` â€” in particular:

| Take | Leave |
|---|---|
| `src/styles/` â€” the nine partials, a real design system | `src/app/analysis-results/`, `investment/`, `dashboard/`, `beheer/`, `management/` |
| `src/app/background-task-status/` â€” the progress popover, reworked | `src/app/models/` â€” all stock domain |
| `angular.json`, `tsconfig*.json` shape | `src/app/services/*` â€” replaced by the adapter |
| The shell: `app.ts`, `app.html`, `app.routes.ts`, `app.config.ts` | `Program.cs`, `WebApp.csproj` â€” never deployed (analysis Â§1) |

**Drop `@microsoft/signalr`.** It has zero imports anywhere in `WebApp/src` and the hub it
targets was never implemented (analysis Â§1, Â§4).

## Tasks

### 1. Scaffold

```powershell
cd C:\Dev\AppPlatform\starters
npx @angular/cli@latest new angular --style=scss --routing --ssr=false --skip-git
```

Then `npm i @PS/app-client @PS/app-client-angular @azure/msal-browser bootstrap`.

Keep `bootstrap` â€” the source design system is built on it (`_bootstrap-overrides.scss`) and
discarding that means rewriting nine SCSS partials for no benefit.

### 2. Port the design system

Copy `src/styles/` verbatim: `_variables`, `_fonts`, `_bootstrap-overrides`, `_layout`,
`_menu`, `_buttons`, `_tables`, `_kpis`, `_popover`.

Review each for stock-specific rules â€” `_kpis.scss` in particular may carry finance-shaped
assumptions. Generalise class names where they name a domain concept; leave the rest alone.

This is the one part of the front-end with real accumulated value. Do not rewrite it.

### 3. `app.config.ts`

```ts
export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideHttpClient(withInterceptors([platformAuthInterceptor])),
    provideAppPlatform(),
  ],
};
```

Five lines. That is the point of Step 24.

### 4. The shell

Port `app.html` and `app.ts`, stripped to: a navbar with the app title from config, the
`<PS-task-progress>` popover, a sign-in/sign-out control, and `<router-outlet>`.

Two starter routes: `/` (a home page showing config and auth state) and `/example` (a list
page backed by `useApi`-equivalent calls). `/example` exists specifically so the deep-link
fallback from Step 09 can be verified with a real router.

### 5. Auth UI â€” **the gap this closes**

The source `WebApp` **cannot authenticate at all** â€” no MSAL, no interceptor, no guards
(analysis Â§6). Enabling backend enforcement today would lock it out entirely.

Add:
- a sign-in button calling `AuthClient.signIn()`
- the signed-in account name and a sign-out control
- a `canActivate` guard redirecting to sign-in for protected routes
- a friendly 403 page distinguishing "you are not signed in" from "you lack the required
  role" â€” the latter is unfixable by the user and should say who to ask

### 6. Background task UI

Port `background-task-status/` to use `BackgroundTaskService` from the adapter. Keep the
popover behaviour: a navbar indicator that expands to per-task progress bars.

Delete the source's own polling code â€” it lives in `TaskPoller` now.

### 7. `configuration.json` for development

`public/configuration.json` lets `ng serve` run without the backend:

```json
{ "api": { "root": "http://localhost:7071/api" }, "title": "Angular Starter" }
```

The deploy workflow deletes this file before upload (the source workflow already does), so
the deployed app reads the real `/configuration.json` from the API. Note that in the README â€”
it is a genuinely confusing arrangement on first encounter.

### 8. Wire into the template

Add `starters/angular` as the `Frontend=angular` content for `dotnet new tinyapp` (Step 18),
scaffolded to `WebApp/`, with `sourceName` replacement covering the app title.

### 9. README

Getting started, how config resolution works in dev vs deployed, how to add a page, how to
call the API, and how to start a background task.

## Verification

```powershell
cd C:\Dev\AppPlatform\starters\angular
npm install
npm run build
```

**Expected:** builds clean.

Then run it against the sample backend:

```powershell
# shell 1
cd C:\Dev\AppPlatform\samples\SampleApp; func start
# shell 2
cd C:\Dev\AppPlatform\starters\angular; npm start
```

Browse `http://localhost:4200`:

- [ ] The title comes from `/configuration.json`
- [ ] The notes list loads from `/api/notes`
- [ ] Starting a background task shows progress that advances to completion
- [ ] No console errors, and no CORS errors

Then the deployed-shape check â€” build and serve through the backend:

```powershell
npm run build
# point staticContent:files:rootPath at starters/angular/dist/angular/browser, restart func
```

- [ ] `http://localhost:7071/` loads the app
- [ ] **`http://localhost:7071/example` on a hard refresh loads the app**, not a 404

That last check is the Â§4 fix verified with a real router, which is what it was always for.

Finally confirm SignalR is gone:

```powershell
Select-String -Path package.json,src -Pattern 'signalr' -Recurse
```

**Expected:** no matches.

## Done when

- [ ] Builds clean and runs against the sample backend
- [ ] The design system is ported, with domain-specific rules generalised
- [ ] `app.config.ts` is ~5 lines of providers
- [ ] Sign-in, sign-out, a route guard and a role-aware 403 page all work
- [ ] Task progress advances to completion
- [ ] A hard refresh on a deep link serves the app
- [ ] No stock domain code and no `@microsoft/signalr` remain
- [ ] Wired into the template as `Frontend=angular`

## Commit

```powershell
git add -A
git commit -m "Step 26: Angular starter with design system, MSAL sign-in and task progress"
```

