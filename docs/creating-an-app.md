# Creating a new app

This guide takes you from nothing to a deployed app on the platform: an Azure Functions back-end, an
Angular front-end, sign-in with Microsoft Entra, and users, tenants and roles from the registry.

**Every app built this way uses sign-in and the multi-tenancy library.** The rest of this guide
assumes `--tenancy single` or `--tenancy multi` and `--frontend angular`. (The template also has
`--tenancy none` and other front-ends; they are outside this guide.)

1. [What you get](#what-you-get)
2. [Before you start](#before-you-start)
3. [Step 1: Generate the app](#step-1-generate-the-app)
4. [Step 2: Run it locally](#step-2-run-it-locally)
5. [Step 3: Build your app](#step-3-build-your-app)
6. [Step 4: Provision, configure and deploy](#step-4-provision-configure-and-deploy)
7. [Step 5: Invite users](#step-5-invite-users)
8. [Choosing Single or Multi](#choosing-single-or-multi)
9. [Packages](#packages)
10. [Troubleshooting](#troubleshooting)

## What you get

| Part | What it is | More |
|---|---|---|
| Back-end | Azure Functions (.NET 10) on `PS.AppPlatform` and `PS.AppPlatform.Functions`: default-deny API, SQL migrations, background tasks, static hosting of the SPA | [PS.AppPlatform README](../src/PS.AppPlatform/README.md) |
| Registry client | `AddPlatformTenancy`: resolves the signed-in user and their tenant on every request and replaces token roles with registry roles | [multi-tenancy.md](multi-tenancy.md) |
| Front-end | The Angular starter, wired to `@patrickschop/app-client` and `@patrickschop/app-client-angular`: sign-in (MSAL), route guards, tenant picker and switcher, task progress | [starters/angular/README.md](../starters/angular/README.md) |
| Management app | A separate, shared app (`apps/Management`) where administrators manage users, tenants, teams and roles for every app | [multi-tenancy.md](multi-tenancy.md#concepts) |

How a request flows: the SPA signs the user in with MSAL, asks `GET /api/me/tenants` which tenants
they belong to, lets them choose one when there are several, and sends the choice in `X-Tenant-Id`
with every call. The back-end validates the token, looks the user up in the registry (through the
management app), and only then runs your endpoint. Your code sees an `ITenantContext` and the user's
roles in that tenant. See [The login flow](multi-tenancy.md#the-login-flow).

## Before you start

**Tools** (full list with notes in the [README](../README.md#prerequisites)): .NET SDK 10, Node 24,
Azure Functions Core Tools v4, Azure CLI, Azurite (required, start it before `func start`) and SQL
Server LocalDB.

**Access to the packages.** The `PS.AppPlatform*` NuGet packages and the `dotnet new` template live
on GitHub Packages, which needs a token even to read public packages. Set up a classic personal
access token with `read:packages` once per machine, as described in
[consuming-packages.md](consuming-packages.md).

**Already done by an operator**, once for the whole platform:

- The shared Entra app registrations (PS Apps API and PS Apps SPA): [auth-setup.md](auth-setup.md), Part 1.
- The management app is deployed and has an administrator
  ([operator runbook](multi-tenancy.md#operator-runbook)).

You need from them: the client ids of the API and SPA registrations, the management app's URL, and its
audience (`api://<client id of PS Apps API>`).

## Step 1: Generate the app

Install the template, from the [PS.AppPlatform.Templates](https://github.com/PatrickSchop/AppPlatform/packages)
package on GitHub Packages:

```powershell
dotnet new install PS.AppPlatform.Templates
```

Create the app. `Recipes` is the example name; `--azure-app-name` is the lowercase name its Azure
resources will have (the `appName` of `infra/app.bicep`):

```powershell
dotnet new tinyapp -n Recipes `
    --tenancy multi `
    --frontend angular `
    --azure-app-name recipes `
    --management-url https://<management app>.azurewebsites.net `
    --management-audience api://<client id of PS Apps API>
cd Recipes
```

Leave out `--app-role`: with the registry, roles come from the registry, not from Entra App Roles.
Template options are listed in [templates/README.md](../templates/README.md).

What was generated:

| Path | Purpose |
|---|---|
| `Manifest.cs` | The app's key, tenancy mode and roles (`editor`, `viewer`). Sent to the management app on every deploy |
| `Program.cs`, `Recipes.csproj` | `AddPlatformTenancy` and `<PlatformTenancy>true</PlatformTenancy>`: always together |
| `Data/`, `Database/Scripts/` | EF Core context and SQL migrations; an example tenant-owned `Item` |
| `Api/` | Example endpoints: `GET /api/items` (any registered user), `POST /api/items` (role `editor`) |
| `appsettings.json` | `webApp` (public config for the SPA), `authentication`, `tenancy` |
| `appsettings.development.json` | A local user directory, so you can run without the management app |
| `WebApp/` | The Angular starter |
| `.github/workflows/deploy.yaml` | Builds, registers the app with the management app, then deploys |
| `README.md`, `docs/deployment.md` | The generated app's own documentation |

## Step 2: Run it locally

### Back-end

1. Start Azurite: `azurite --location <some folder>`.
2. Create an empty database called `Recipes` on LocalDB.
3. In `appsettings.development.json`, replace the zero `oid` and `tid` of the example user with
   yours. A user is identified by `oid` + `tid`, never by email. You can read both from your
   token, or from the "No access" page of the management app.
4. Set the sign-in values in `appsettings.json` (see [Sign-in settings](#sign-in-settings)).
5. Build, migrate and start:

```powershell
dotnet build
dotnet exec bin/Debug/net10.0/Recipes.dll --migrate
func start
```

Locally the registry is the file `appsettings.development.json` (`tenancy:directory = config`); in
Azure it is the management app (`tenancy:directory = management`). Your code is identical in both.

### Sign-in settings

Two sections, for two different audiences. Do not mix them up:

```json
"webApp": {
  "auth": {
    "tenantId": "common",
    "clientId": "<PS Apps SPA client id>",
    "scopes": [ "api://<PS Apps API client id>/access_as_user" ]
  }
},
"authentication": {
  "azureEntraId": {
    "tenantId": "common",
    "clientId": "<PS Apps API client id>",
    "additionalAudiences": [ "api://<PS Apps API client id>" ]
  },
  "requiredRole": ""
}
```

- `webApp` is **public**: it is served unauthenticated at `/configuration.json`, and is what the SPA
  signs in with. The client id here is the **SPA** registration. Never put secrets here
  ([security.md](security.md)).
- `authentication` is what the API validates tokens against, the **API** registration.
- Leave `requiredRole` empty. Access is controlled by the registry (a user must be registered and
  have a role), not by an Entra App Role. `tenantId: common` accepts work, school and personal
  Microsoft accounts. See [auth-setup.md](auth-setup.md) for the registrations and the
  [tenantId/clientId trap](auth-setup.md#the-tenantidclientid-trap).

You do not set `webApp:tenancy`: the platform adds it to `/configuration.json` from the `tenancy`
settings, and the SPA reads it to decide whether to run the tenant flow.

### Front-end

```powershell
cd WebApp
npm install
npm start          # http://localhost:4200, /api is proxied to http://localhost:7071
```

`npm install` needs a token for the `@patrickschop/*` packages, see [npm packages](consuming-packages.md#npm-packages).

By default `ng serve` uses `WebApp/public/configuration.json`, which has no sign-in settings, so the
app starts but runs signed out. To develop the real sign-in and tenant flow, copy the running
back-end's configuration over it (and do not commit the copy):

```powershell
curl http://localhost:7071/configuration.json -o public/configuration.json
```

Then open the app, sign in, and you land on the picker (several tenants) or the app (one).
The starter's README, [starters/angular/README.md](../starters/angular/README.md), explains how
configuration is resolved in development versus deployed.

## Step 3: Build your app

### Back-end

The generated README walks through entities, endpoints and background tasks. The tenancy-specific
parts:

- **Tenant-owned data:** derive entities from `TenantEntity`, not `Entity`. Queries are filtered to
  the current tenant and inserts are stamped with it; you never mention the tenant in code. Add a
  `TenantId` column in your migration script. See
  [Tenant-aware data](multi-tenancy.md#tenant-aware-data), including which `DbContext` factory to
  use where.
- **Roles:** declare them in `Manifest.cs` and check them with `[Authorize(Roles = "editor")]`. They
  are the user's registry roles in the selected tenant.
- **Endpoints:** every `/api/*` function is default-deny. Keep `[Authorize]` on yours. Anonymous
  endpoints and endpoints without a tenant (`[TenantOptional]`) are explained in
  [multi-tenancy.md](multi-tenancy.md#tenant-resolution).
- **Background tasks:** task lists are per tenant; the SPA polls only once a tenant is selected.

When you add a role to `Manifest.cs`, it appears in the management app on the next deploy. A role you
remove is deprecated, not deleted ([Registration](multi-tenancy.md#registration)).

### Front-end

The starter is deliberately small. In `WebApp/src/app`:

| To do this | Do this |
|---|---|
| Add a page | Add a standalone component under `pages/`, a route in `app.routes.ts` inside the group guarded by `authGuard` and `tenantGuard`, and a link in `app.component.html` |
| Call the API | Inject `API_CLIENT` (see `services/example.service.ts`). It adds the access token and `X-Tenant-Id` to every request |
| Start a background task | `BackgroundTaskService.createTask(type, data)`; progress appears in the top bar |
| Hide UI by role | `TenantService.hasRole('editor')`. This only hides UI; the API enforces the role |
| Change the title | `webApp:title` in `appsettings.json` |

Delete the example page and service when you have your own. The look follows the management app:
semantic colour tokens in `src/styles.scss`, a top bar and a sidebar.

## Step 4: Provision, configure and deploy

Per app, once:

1. **Provision Azure resources** with `infra/app.bicep`, passing `managementUrl` and
   `managementAudience` so the running app can ask the registry for its users. The steps are in
   [provisioning.md](provisioning.md); do them in that order, including the database user.
2. **Allow the app's origin** in the SPA registration's redirect URIs
   ([auth-setup.md](auth-setup.md), Part 2.3).
3. **Trust the deployer.** Add the object id of the principal that runs your deploy workflow to
   `registry__trustedDeployers__<n>` on the management Function App. Without it, the deploy fails
   with a 403 before publishing anything.
4. **Check the workflow.** `.github/workflows/deploy.yaml` has `register: true`, and the management
   URL and audience you passed at generation. Secrets and OIDC setup are in the generated
   `docs/deployment.md`.
5. **Set the production settings** for `webApp:auth` and `authentication` on the Function App, using
   `__` as the separator (`webApp__auth__clientId`), not `:`.

Then deploy:

```powershell
git push origin main
```

The workflow builds the back-end, registers the app and its roles with the management app, and
deploys. The app should then be listed in the management app under Applications.

Easy Auth must stay **off**; the platform authenticates in-process
([auth-setup.md](auth-setup.md#app-service-easy-auth-must-be-off)).

## Step 5: Invite users

Nobody can use the app until they are registered. In the management app: Users, Create user, choose
the tenant, team and roles, and send them the invitation link (shown once). The user opens it,
signs in and accepts. The user's first visit to your app then shows the app, or the tenant picker
if they belong to several tenants. The full procedure, and what to do when the link expires, is
in the [operator runbook](multi-tenancy.md#operator-runbook).

Changes in the management app reach a running app within about five minutes
([Caching and consistency](multi-tenancy.md#caching-and-consistency)).

Users from another Entra organisation need that organisation's administrator to consent first
(error `AADSTS500011`).

## Choosing Single or Multi

| | `single` | `multi` |
|---|---|---|
| Tenants | exactly one, always selected | several; the user chooses |
| Picker and switcher | never shown | shown when a user has more than one tenant |
| Use it for | a private app: registered users and roles, no customers | a SaaS-style app with separate customers |

Changing from `multi` to `single` is refused while more than one tenant is enabled. Start with
`multi` if you might need it; the app code is the same in both.

## Packages

| Package | Where | Used for |
|---|---|---|
| `PS.AppPlatform` | NuGet, GitHub Packages | Core: data layer, tasks, auth, tenancy |
| `PS.AppPlatform.Functions` | NuGet, GitHub Packages | Azure Functions endpoints injected into your app |
| `PS.AppPlatform.Templates` | NuGet, GitHub Packages | `dotnet new tinyapp` |
| `@patrickschop/app-client` | [`clients/app-client`](../clients/app-client) | Config, API client, MSAL sign-in, `TenantSession`, task poller |
| `@patrickschop/app-client-angular` | [`clients/app-client-angular`](../clients/app-client-angular) | Angular providers, guards, tenant picker/switcher, task progress |

Published versions are on the
[repository's Packages page](https://github.com/PatrickSchop/AppPlatform/packages). Keep the NuGet
packages on the same version: they are released in lockstep ([versioning.md](versioning.md)).

The npm packages are published to the same GitHub Packages by the
[publish workflow](../.github/workflows/publish.yaml), in lockstep with the NuGet packages. Their scope
is `@patrickschop` rather than `@PS` because GitHub Packages requires an npm scope to equal the
owning account. A generated app's `WebApp/.npmrc` already points that scope at GitHub Packages;
you only need a token on your machine, see
[npm packages](consuming-packages.md#npm-packages). The generated app has no `package-lock.json`;
the build installs with `npm install` until you commit one (run `npm install` in `WebApp` and
commit the result, after which the build uses `npm ci`).

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| 401 on every call | Sign-in settings wrong or the SPA scope does not match the API; see [auth-setup.md](auth-setup.md) |
| 403 `not_registered` | The user is not in the registry, or the `oid`/`tid` in the dev directory is not yours |
| 403 `tenant_forbidden` / 409 `tenant_required` | The SPA sent no tenant or a wrong one; the client recovers by showing the picker ([error codes](multi-tenancy.md#tenant-resolution)) |
| 503 `registry_unavailable` | The app cannot reach the management app; check `tenancy__management__*` settings |
| Deploy stops at registration | The deployer is not in `registry__trustedDeployers`, or `management_url` / `management_audience` is empty |
| Host never starts locally | Azurite is not running ([README](../README.md#prerequisites)) |
| Restore fails with 401/403 | Package authentication, see [consuming-packages.md](consuming-packages.md) |
| `/configuration.json` returns HTML | Easy Auth is on; see [provisioning.md](provisioning.md#troubleshooting) |

## See also

- [multi-tenancy.md](multi-tenancy.md): concepts, login flow, registration and operator runbook
- [auth-setup.md](auth-setup.md): Entra app registrations
- [provisioning.md](provisioning.md): Azure resources and first deploy
- [security.md](security.md): what may go in public configuration
- [versioning.md](versioning.md): version policy
