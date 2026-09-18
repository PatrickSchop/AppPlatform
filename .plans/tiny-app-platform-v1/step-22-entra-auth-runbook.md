# Step 22 â€” Entra auth runbook

**Phase:** 4 â€” Template and infrastructure
**Depends on:** Step 21
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Write the operator-executed runbook for the shared app-registration model, and act on the
security items from analysis Â§6.

The code was built in Step 10. This step is **documentation plus one manual Azure procedure**,
because creating app registrations and App Roles needs tenant admin and should not be
automated into CI (that was the scope decision).

## The model (analysis Â§5)

One shared app-registration **pair** â€” one SPA client and one API â€” with a **per-app App Role**
(`stock.user`, `recipes.user`, â€¦). The core enforces a required role from config. One consent,
one client id, one line of config per app.

**State the trade-off honestly in the doc**, because it is a deliberate choice and not free:
all apps share a token audience, so separation rests entirely on the role check. A token
issued for one app is cryptographically valid for another; only the `roles` claim stops it.
At side-project scale that is acceptable. It would not be if apps had meaningfully different
data sensitivity. Write it down so the choice is re-examinable rather than forgotten.

## Tasks

### 1. `docs/auth-setup.md` â€” the runbook

**Part 1 â€” one-time tenant setup:**

1. Create the API app registration `PS Apps API`.
   - Set the Application ID URI to `api://<api-client-id>`.
   - Add a scope `access_as_user`, admin-consent only.
2. Create the SPA app registration `PS Apps SPA`.
   - Platform: Single-page application.
   - Redirect URIs: `http://localhost:4200`, `http://localhost:5173`, and each app's
     production origin.
   - API permissions: `PS Apps API / access_as_user`, then grant admin consent.
3. Record both client ids and the tenant id in a password manager, not in a repo.

Give the `az` equivalents for each step alongside the portal instructions â€” the portal UI
changes and the commands do not.

**Part 2 â€” per app:**

1. Add an App Role to the **API** registration:
   - Display name: `Recipes user`
   - Value: `recipes.user`
   - Allowed member types: Users/Groups
2. Assign the role to users or a group under Enterprise applications â†’ PS Apps API â†’
   Users and groups.
3. Add the app's production origin to the SPA registration's redirect URIs.
4. Set the app's configuration:
   ```json
   "authentication": {
     "azureEntraId": {
       "tenantId":  "<tenant guid>",
       "clientId":  "<API app client id>",
       "additionalAudiences": [ "api://<API app client id>" ]
     },
     "requiredRole": "recipes.user"
   }
   ```

**Part 3 â€” the tenantId / clientId trap.**

Document it with the real example, since it is in the source repo:
`C:\Dev\StockAnalysis\App\appsettings.json` has `tenantId` and `clientId` both set to
`6d91dfaa-31e9-4a32-84b0-c85f830ecc5a` (analysis Â§6). Audience validation then looks for the
tenant GUID as the audience and every token is rejected, with an error message that does not
point at the cause.

Step 10 makes this fail at **startup** with a message naming both keys. Say so, so that
whoever hits the startup error finds this page.

**Part 4 â€” the security model.** State it plainly and up front, because every later decision
follows from it:

> **The API is the security boundary.** Every `/api/*` endpoint is default-deny; no data is
> reachable unauthorized. The SPA bundle is served to anyone who asks, and that is
> deliberate â€” there is nothing sensitive in the HTML and JavaScript, and a browser's first
> navigation carries no bearer token anyway. A front-end that mishandles a 401 or 403 looks
> ugly; it does not leak anything.

Consequences worth spelling out:

- `StaticContent`, `GetHealth` and `GetWebAppConfiguration` are `[AllowAnonymous]`.
  Everything else is protected by default.
- **Anything placed under `webApp` in configuration is public.** It is served unauthenticated
  at `/configuration.json`. No secrets, no internal hostnames, no tenant details beyond the
  client and tenant ids the SPA needs to sign in.
- The front-end's obligation is UX, not enforcement: handle 401 by prompting sign-in, handle
  403 by explaining that the user lacks the app's role and naming who can grant it.
- Adding `[AllowAnonymous]` to a new endpoint is a security decision. It deserves a
  code-review comment saying why.

**Part 4a â€” App Service Easy Auth must be OFF.**

The platform authenticates in-process (Step 10). **App Service Authentication ("Easy Auth")
is not used and must stay disabled on every platform app.** Step 20's bicep asserts this
declaratively; this section explains why and how to confirm it.

Two independent auth layers is not defence in depth here â€” it is a broken app. Easy Auth sits
in front of the worker and intercepts requests before any platform code runs, which breaks
the security model in Part 4 in three specific ways:

| Easy Auth setting | What breaks |
|---|---|
| `unauthenticatedClientAction: RedirectToLoginPage` | Every anonymous request becomes a **302**, so `/api/*` returns a redirect instead of a 401. The SPA's `fetch` follows it, receives the login **HTML**, and fails with a JSON parse error that names nothing relevant. |
| `unauthenticatedClientAction: Return401` | Closer, but the 401 comes from the platform layer with no `Access-Control-Allow-Origin`, so the browser reports an opaque CORS error â€” the exact symptom Step 10 exists to eliminate. |
| Either | `/configuration.json` and `/api/health` stop being anonymous, so the SPA cannot bootstrap and health probes fail. |

**The diagnostic signature to remember:** if a deployed app's SPA fails with
`Unexpected token '<' ... is not valid JSON` while everything works locally, Easy Auth is on.
That error points at JSON parsing and has nothing visibly to do with authentication, which is
why it is worth writing down.

**Verify on every app, at provisioning and after each deploy:**

```powershell
az webapp auth show --resource-group Applications --name <app>-api `
  --query "{enabled:enabled, action:unauthenticatedClientAction}" -o json
```

**Expected:** `enabled: false` (or the command reports no auth settings configured).
Anything else â€” fix it before going further:

```powershell
az webapp auth update --resource-group Applications --name <app>-api --enabled false
```

**Separately: explain the StockAnalysis redirect.**

`stockanalysis.PS.nl` redirects to a login page, but per analysis Â§6 the in-app
`[Authorize]` cannot be doing that â€” a failed `[Authorize]` in the isolated worker produces a
401, not a redirect. Easy Auth is the most likely explanation. Run the same check against
`stockanalysis-api` and record the answer in `docs/security.md`.

This is diagnostic only and **changes nothing about the plan** â€” the platform assumes Easy
Auth is off either way. But the answer matters for two reasons:

- **If it is enabled**, then Easy Auth â€” not the application â€” has been providing whatever
  protection existed, confirming Â§6's finding that the app enforces nothing. It also means
  that whenever StockAnalysis is migrated onto the platform (out of scope for v1), turning
  Easy Auth off and turning in-app auth on must happen in the same change, or there is a
  window with no protection at all. Write that down for the future migration plan.
- **If it is not enabled**, the redirect comes from somewhere else â€” a front door, DNS, or
  the SPA's own routing â€” and is worth pinning down. Unexplained auth behaviour in production
  is not something to carry forward into a platform other apps will inherit.

Do not change anything on `stockanalysis-api`. This is a read-only check; that app is out of
scope (container Â§2.1).

**Part 5 â€” troubleshooting.** The failures that will actually happen:

| Symptom | Cause |
|---|---|
| 401 on every request, valid-looking token | Audience mismatch â€” add `api://<clientId>` to `additionalAudiences` |
| 403 with a valid token | The App Role is not assigned to the user, or `requiredRole` does not match the role `value` |
| Startup throws naming tenantId and clientId | The Part 3 copy-paste error |
| An opaque CORS error instead of a 401 | CORS ordering â€” should be impossible after Step 10; if seen, `UsePlatform()` was not called |
| `roles` claim absent from the token | The role was added to the SPA registration instead of the API registration |
| **`Unexpected token '<' ... is not valid JSON` in the browser, deployed only** | **Easy Auth is enabled â€” `/configuration.json` is returning a login page. See Part 4a.** |
| **302 where a 401 or 200 was expected** | **Easy Auth again, with `RedirectToLoginPage`.** |

Two of these are worth extra attention. The `roles`-on-the-wrong-registration mistake is
genuinely common and produces a token that looks perfectly fine. And the JSON parse error is
the one nobody diagnoses quickly, because the message points at parsing and the cause is
authentication infrastructure the app never asked for â€” note that it appears **only when
deployed**, since Easy Auth does not exist locally.

### 2. Rotate the leaked key â€” **do this, do not just document it**

A **live Azure Cognitive Services API key is committed** at
`C:\Dev\StockAnalysis\App\appsettings.json:25` and is **in that repository's git history**,
not just the working tree (analysis Â§6).

Required actions, in order:

1. **Rotate the key now** â€” Azure portal â†’ the Cognitive Services account â†’ Keys and
   Endpoint â†’ Regenerate Key 1. Do this before anything else; every minute it stays valid is
   exposure.
2. Update whatever currently consumes it to use the new key **from Function App settings**,
   not from `appsettings.json`.
3. Consider switching that account to managed-identity auth entirely, which removes the
   category of problem. Step 11 already warns when `apiKey` auth is configured.
4. Treat the old key as compromised regardless of repository visibility. Rewriting git
   history is optional and disruptive; rotation is what actually matters.

Record in `docs/security.md` that this was done and when. If it has **not** been done, say so
plainly in that file rather than leaving it ambiguous.

This step's scope is the platform repo, and the key lives in StockAnalysis â€” but the
rotation is a portal action, not a repo edit, so it is doable here without violating the
read-only rule. Do not edit any StockAnalysis file.

### 3. `docs/security.md`

- Secrets policy: Function App settings or Key Vault references. Never `appsettings.json`.
  Point at the incident above as the reason the rule exists.
- The shared-audience trade-off from the top of this document.
- Default-deny: new endpoints are protected unless they opt out, and adding
  `[AllowAnonymous]` is a decision worth a code-review comment.
- The two gates on database initialisation (Step 12).
- What the platform logs and does not log: never a token, never a connection string; the
  caller `oid` on a 403 only.

### 4. Add a secret scan to CI

```yaml
- name: Scan for secrets
  uses: gitleaks/gitleaks-action@v2
  env:
    GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

Add a `.gitleaks.toml` allowing the placeholder GUIDs in template content so it does not
false-positive on `TINYAPP-NAME` style tokens.

The platform repo is the one place this can be enforced going forward, and the whole reason
it is worth enforcing is sitting in the source repo's history.

### 5. Update the template

Make sure `dotnet new tinyapp --AppRole recipes.user` produces an `appsettings.json` whose
`authentication` block is correctly shaped, with empty GUIDs and a comment pointing at
`docs/auth-setup.md`. Verify no real tenant or client id ever ships in template content.

## Verification

```powershell
cd C:\Dev\AppPlatform
Select-String -Path templates\content\**\*.json -Pattern '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
```

**Expected:** no matches â€” no GUID in any template file.

```powershell
Select-String -Path . -Pattern '6d91dfaa' -Recurse
```

**Expected:** matches only inside `.plans/` and `docs/auth-setup.md`, where it is quoted as a
cautionary example. **Never in code or configuration.**

Then walk the runbook end to end for one real app â€” this happens naturally in Step 19 Check 8.
The runbook is only verified when someone has followed it without needing to guess.

## Done when

- [ ] `docs/auth-setup.md` covers one-time setup, per-app setup, the tenantId trap, the
      security model, the Easy Auth check and the five troubleshooting cases
- [ ] The shared-audience trade-off is stated explicitly, not implied
- [ ] **The Cognitive Services key has been rotated**, and `docs/security.md` records it
- [ ] `docs/security.md` states the secrets policy and the logging policy
- [ ] CI scans for secrets
- [ ] No GUID appears in any template file
- [ ] `6d91dfaa` appears only in plan and doc prose

## Commit

```powershell
git add -A
git commit -m "Step 22: Entra auth runbook, security policy, secret scanning"
```

