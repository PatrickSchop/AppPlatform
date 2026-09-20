# Azure Entra ID Authentication Setup

This runbook sets up authentication for TinyApp platform apps using a shared app registration model with per-app roles.

## Overview

- **One shared setup:** An API app registration and SPA app registration created once by a tenant admin
- **Per-app configuration:** Each app gets its own Entra App Role (e.g., `recipes.user`)
- **One consent:** The SPA has been granted access to the API once
- **Result:** New apps need only config changes and role assignment, not new app registrations

### Security Model

> **The API is the security boundary.** Every `/api/*` endpoint is default-deny; no data is reachable unauthorized. The SPA bundle is served to anyone who asks, and that is deliberate — there is nothing sensitive in the HTML and JavaScript, and a browser's first navigation carries no bearer token anyway. A front-end that mishandles a 401 or 403 looks ugly; it does not leak anything.

**Consequences:**

- `StaticContent`, `GetHealth`, and `GetWebAppConfiguration` are `[AllowAnonymous]`; everything else is protected by default
- **Anything under `webApp` in configuration is public** — served at `/configuration.json` unauthenticated
- The front-end's obligation is UX, not enforcement: handle 401 by prompting sign-in, handle 403 by explaining the user lacks the role
- Adding `[AllowAnonymous]` to a new endpoint is a security decision deserving a code-review comment saying why

### The Trade-off

All apps share a token audience, so **separation rests entirely on the role check.** A token issued for one app is cryptographically valid for another; only the `roles` claim stops it. At side-project scale this is acceptable. It would not be if apps had meaningfully different data sensitivity.

---

## Part 1: One-Time Tenant Setup

A tenant admin performs these steps **once** for all apps.

### Step 1.1: Create the API app registration

#### Portal

1. Go to **Azure AD** → **App registrations** → **New registration**
2. Name: `PS Apps API`
3. Register
4. In the app, go to **Expose an API**
5. Click **Set** next to Application ID URI
6. Accept the default (`api://<client-id>`), or customize it
7. Click **Save**
8. Under **Scopes defined by this API**, click **Add a scope**
   - Scope name: `access_as_user`
   - Who can consent: **Admin only**
   - Admin consent display name: `Access as user`
   - Click **Add scope**
9. Note the **Application (client) ID** — you will need this for the SPA and every app config

#### Command line

```bash
# Create the app registration
az ad app create --display-name "PS Apps API" \
  --identifier-uris "api://$(az ad app create --display-name dummy | jq -r .appId)"

# Note the appId from the output

# Get the app's service principal
APP_ID="<the client id from above>"
az ad sp create --id $APP_ID

# Add the scope
az ad app permission grant-admin-consent --id $APP_ID \
  --resource-owner-id $(az ad signed-in-user show --query id -o tsv)
```

### Step 1.2: Create the SPA app registration

#### Portal

1. **Azure AD** → **App registrations** → **New registration**
2. Name: `PS Apps SPA`
3. Platform: **Single-page application**
4. Redirect URIs:
   - `http://localhost:4200` (Angular dev)
   - `http://localhost:5173` (React/Vite dev)
   - Add each app's production origin later (e.g., `https://recipes.PS.nl`)
5. Register
6. Go to **API permissions** → **Add a permission** → **APIs my organization uses** → search for `PS Apps API`
7. Select **access_as_user** scope
8. **Grant admin consent** (the button at the bottom of the API permissions blade)
9. Note the **Application (client) ID** — this is the `clientId` for every app's SPA

#### Command line

```bash
# Create the app
az ad app create --display-name "PS Apps SPA" \
  --public-client-redirect-uris "http://localhost:4200" "http://localhost:5173"

# Note the appId

# Add API permission
API_APP_ID="<PS Apps API client id from Step 1.1>"
SPA_APP_ID="<the SPA app id from above>"
az ad app permission add --id $SPA_APP_ID \
  --api $API_APP_ID \
  --api-permissions "e1ff7924-8401-4de4-8d76-7b755eac1d8f=Scope"

# Grant admin consent
az ad app permission admin-consent --id $SPA_APP_ID
```

### Step 1.3: Record credentials

Store these securely in a password manager — **never in a repo**:

- **PS Apps API** client ID
- **PS Apps SPA** client ID  
- Your **Tenant ID**

---

## Part 2: Per-App Setup

For each new app:

### Step 2.1: Create an App Role on the API registration

#### Portal

1. Go to **Azure AD** → **App registrations** → **PS Apps API**
2. **App roles** → **Create app role**
3. Display name: `Recipes user` (or `<App> user`)
4. Value: `recipes.user` (or `<appname>.user` — **must match config**, case-sensitive)
5. Allowed member types: **Users/Groups**
6. Click **Apply**

#### Command line

```bash
API_APP_ID="<PS Apps API client id>"
az ad app role create --id $API_APP_ID \
  --display-name "Recipes user" \
  --value "recipes.user" \
  --enabled \
  --type User
```

### Step 2.2: Assign the role to users

#### Portal

1. **Azure AD** → **Enterprise applications** → search for `PS Apps API`
2. **Users and groups** → **Add user/group**
3. Select your user or a group
4. **Select role** → pick `Recipes user` (or the one you just created)
5. Assign

#### Command line

```bash
API_APP_ID="<PS Apps API client id>"
USER_OID="<your object id>"
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/${API_APP_ID}/appRoleAssignedTo" \
  --body "{\"principalId\":\"${USER_OID}\",\"appRoleId\":\"<role id>\",\"resourceId\":\"<service principal id>\"}"
```

### Step 2.3: Add the app's production origin to the SPA

#### Portal

1. **Azure AD** → **App registrations** → **PS Apps SPA**
2. **Authentication** → **Single-page application**
3. Under **Redirect URIs**, add your app's origin (e.g., `https://recipes.PS.nl`)
4. Save

#### Command line

```bash
SPA_APP_ID="<PS Apps SPA client id>"
az ad app update --id $SPA_APP_ID \
  --public-client-redirect-uris "http://localhost:4200" "http://localhost:5173" "https://recipes.PS.nl"
```

### Step 2.4: Configure the app

Set these in the app's configuration (Function App settings or `appsettings.json` for local dev):

```json
{
  "authentication": {
    "azureEntraId": {
      "tenantId": "<your tenant guid>",
      "clientId": "<PS Apps API client id from Step 1.1>",
      "additionalAudiences": ["api://<PS Apps API client id>"]
    },
    "requiredRole": "recipes.user"
  }
}
```

---

## The tenantId/clientId Trap

`C:\Dev\StockAnalysis\App\appsettings.json` has both set to the same GUID: `6d91dfaa-31e9-4a32-84b0-c85f830ecc5a`. This is a copy-paste error.

**Audience validation then looks for the tenant GUID as the audience, and every token is rejected.**

The platform validates this at **startup** and throws a clear error if `tenantId == clientId`. If you see:

```
tenantId and clientId are identical — the copy-paste trap from analysis §6
```

Fix it immediately: `clientId` is the **API app's** client id (from Step 1.1), and `tenantId` is your actual tenant ID (not an app ID).

---

## App Service Easy Auth Must Be OFF

**The platform authenticates in-process.** App Service Authentication ("Easy Auth") is not used and must stay disabled on every platform app.

Easy Auth sits in front of the worker and intercepts requests before any platform code runs, breaking the security model in three specific ways:

| Setting | What breaks |
|---------|------------|
| `unauthenticatedClientAction: RedirectToLoginPage` | Every anonymous request becomes a **302**. `/api/*` returns a redirect instead of a 401; the SPA's `fetch` follows it, receives login **HTML**, and fails with `Unexpected token '<' ... is not valid JSON`. |
| `unauthenticatedClientAction: Return401` | The 401 comes from Easy Auth with no `Access-Control-Allow-Origin`, so the browser reports an opaque CORS error. |
| Either | `/configuration.json` and `/api/health` stop being anonymous. The SPA cannot bootstrap and health probes fail. |

### Verify Easy Auth is OFF

Each app deploys into its own resource group. After deploying:

```powershell
az webapp auth show --resource-group <app-rg> --name <app>-api \
  --query "{enabled:enabled, action:unauthenticatedClientAction}" -o json
```

**Expected:** `enabled: false` or no auth settings at all.

If not, disable it:

```powershell
az webapp auth update --resource-group <app-rg> --name <app>-api --enabled false
```

**Diagnostic:** If a deployed app's SPA fails with `Unexpected token '<' ... is not valid JSON` while everything works locally, Easy Auth is on. That error message points at JSON parsing and has nothing visibly to do with authentication — worth remembering.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| 401 on every request, token looks valid | Audience mismatch | Add `api://<API client id>` to `additionalAudiences` in config |
| 403 with a valid token | Role not assigned or `requiredRole` mismatch | Verify the role was assigned to your user (Part 2.2); verify `requiredRole` matches the role `value` exactly (case-sensitive) |
| Startup error naming tenantId and clientId | Copy-paste error | See the tenantId/clientId trap section above |
| Opaque CORS error instead of 401 | CORS ordering / Easy Auth | Should be impossible after Step 10; if seen, `UsePlatform()` was not called. Or Easy Auth is on. |
| No `roles` claim in token | Role added to SPA registration instead of API | Roles go on the **API** app (PS Apps API), not the SPA |
| **`Unexpected token '<' ...` in browser, deployed only** | **Easy Auth is enabled** | Run the Easy Auth check above and disable it |
| **302 where 401 or 200 expected** | **Easy Auth enabled (RedirectToLoginPage)** | Run the Easy Auth check above and disable it |

---

## Checklist for Each New App

- [ ] **Part 2.1:** App Role created on PS Apps API (`<app>.user`)
- [ ] **Part 2.2:** Role assigned to your user
- [ ] **Part 2.3:** Production origin added to PS Apps SPA redirect URIs
- [ ] **Part 2.4:** App config set with correct tenantId, clientId, and requiredRole
- [ ] **Easy Auth check:** Verified disabled with `az webapp auth show`
- [ ] **Local test:** App runs locally and auth works
- [ ] **Azure test:** Deployed app returns 401 (no token), 403 (wrong role), 200 (correct role)
