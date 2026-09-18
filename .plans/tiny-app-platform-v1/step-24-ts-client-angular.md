# Step 24 â€” `@PS/app-client-angular`

**Phase:** 5 â€” Front-end
**Depends on:** Step 23
**Working directory:** `C:\Dev\AppPlatform\clients\app-client-angular`

## Goal

A thin Angular adapter over `@PS/app-client`. **Thin is the requirement** â€” if this package
grows past ~300 lines of source, logic has leaked out of the core and belongs back in Step 23.

## Tasks

### 1. Package setup

```json
{
  "name": "@PS/app-client-angular",
  "version": "0.1.0",
  "peerDependencies": {
    "@angular/core": ">=19.0.0",
    "@angular/common": ">=19.0.0",
    "@PS/app-client": "^0.1.0"
  }
}
```

Build with `ng-packagr` so it ships as an Angular Package Format library. Target Angular 19+
as the peer range even though the reference app is on 21 â€” there is no reason to exclude 19
or 20 consumers.

During development, resolve `@PS/app-client` via an npm workspace at `clients/package.json`
rather than publishing on every change.

### 2. `provideAppPlatform`

The standalone-API entry point. No NgModule.

```ts
export interface AppPlatformConfig {
  configUrl?: string;
  /** Fail app startup when config cannot be loaded. Default true. */
  required?: boolean;
}

export function provideAppPlatform(config?: AppPlatformConfig): EnvironmentProviders;
```

It must:
- use `provideAppInitializer` (or `APP_INITIALIZER`) to call `createPlatformClient()` before
  the first route resolves, so nothing renders against a half-initialised client
- provide `APP_CONFIG`, `API_CLIENT`, `AUTH_CLIENT` and `BackgroundTaskService` as injection tokens
- register the HTTP interceptor from task 4

### 3. `BackgroundTaskService`

Wraps `TaskPoller` and exposes Angular-native shapes:

```ts
@Injectable({ providedIn: 'root' })
export class BackgroundTaskService implements OnDestroy {
  readonly tasks: Signal<readonly BackgroundTask[]>;
  readonly runningTasks: Signal<readonly BackgroundTask[]>;
  readonly hasRunningTasks: Signal<boolean>;

  /** For templates and code still on observables. */
  readonly tasks$: Observable<readonly BackgroundTask[]>;

  refresh(): Promise<void>;
  expectTaskStart(): void;
  createTask(taskType: string, taskData: unknown, description?: string, requiresNotification?: boolean): Promise<string>;
  ngOnDestroy(): void;
}
```

Signals are the primary surface; `tasks$` exists for migration. Bridge with `toObservable`
from `@angular/core/rxjs-interop` rather than hand-rolling a `BehaviorSubject`.

`ngOnDestroy` must call `poller.stop()` â€” the source service leaks its polling subscription in
some teardown paths, and a root-provided service outliving a test is a real source of flaky
specs.

Note the signature is **identical** to the source's `BackgroundTaskService` where it can be, so
porting the existing `WebApp` (Step 26) is a near-drop-in.

### 4. `platformAuthInterceptor`

A functional `HttpInterceptorFn`:

- attaches the bearer token from `AuthClient` to requests whose URL starts with the configured
  API root
- **never** attaches it to `/configuration.json` or cross-origin URLs â€” leaking a token to a
  third-party host is the failure mode to prevent
- on a 401, calls the configured `onUnauthorized`
- passes through untouched when auth is not configured

### 5. `ConfigService`

```ts
@Injectable({ providedIn: 'root' })
export class ConfigService {
  readonly config: AppConfig;
  get<T>(path: string, fallback?: T): T | undefined;   // dotted path, e.g. 'api.root'
}
```

Replaces the source's `config.service.ts` and `api-configuration.service.ts`, which are two
services doing one thing.

### 6. An optional progress component

`<PS-task-progress>` â€” an unstyled standalone component showing running tasks and their
percentages, with the markup structured for the consumer to style.

Keep it genuinely unstyled: `class` hooks only, no CSS shipped. A starter's design system
(Step 26) should not have to fight the library.

### 7. Tests

`TestBed`-based:

1. `provideAppPlatform` resolves `APP_CONFIG` after initialisation.
2. `BackgroundTaskService.tasks` signal updates when the poller emits.
3. `hasRunningTasks` is true only with a `Running` task.
4. The interceptor adds the bearer header to an API-root URL.
5. **The interceptor does not add it to `/configuration.json`.**
6. **The interceptor does not add it to `https://other-host/x`.**
7. `ngOnDestroy` stops the poller.
8. `ConfigService.get('a.b.c', 'fallback')` resolves and falls back correctly.

## Verification

```powershell
cd C:\Dev\AppPlatform\clients\app-client-angular
npm install
npm run build
npm test
```

**Expected:** ng-packagr produces `dist/`, all tests pass.

Then check the thinness claim:

```powershell
(Get-ChildItem src -Recurse -Include *.ts -Exclude *.spec.ts | Get-Content | Measure-Object -Line).Lines
```

**Expected:** well under 300. If it is over, find what should have been in the core package.

## Done when

- [ ] Builds as an Angular library, tests pass
- [ ] `provideAppPlatform` is standalone-API only, no NgModule
- [ ] Signals primary, `tasks$` available for migration
- [ ] The interceptor never leaks a token to `/configuration.json` or another origin
- [ ] `ngOnDestroy` stops polling
- [ ] Under ~300 lines of non-test source
- [ ] The service signature matches the source's where possible

## Commit

```powershell
git add -A
git commit -m "Step 24: @PS/app-client-angular adapter"
```

