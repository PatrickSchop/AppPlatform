# Security Policy

## Secrets Management

**Rule: Never store secrets in `appsettings.json` or any configuration file.**

Secrets belong in:
- **Function App settings** (deployed via bicep or Azure portal)
- **Azure Key Vault** (referenced via `@Microsoft.KeyVault(SecretUri=...)` syntax)

### Incident

A live Azure Cognitive Services API key was committed to `C:\Dev\StockAnalysis\App\appsettings.json` and is present in that repository's git history. **The key has been rotated** in the Azure portal (Key 1, Cognitive Services account, Keys and Endpoint) as of this document's creation. Any code currently consuming it has been updated to source the key from Function App settings, not from configuration files.

This incident is why the rule above exists. Even if a repository is private, committing secrets to git means:
- The secret is in git history (removing it from the working tree is not enough)
- Every person with clone access has it
- Every pull request CI environment has it
- Every backup has it

**Rotation, not history rewriting, is what matters.** Treat the old key as compromised regardless of repository visibility.

---

## Default-Deny Authorization

Every `/api/*` endpoint is protected by default. New endpoints default to `[Authorize]` unless explicitly opted out.

**Adding `[AllowAnonymous]` to a new endpoint is a security decision and deserves a code-review comment saying why.**

Anonymous endpoints that currently exist:

- **`StaticContent`** — serves the SPA bundle and assets to anyone (nothing sensitive in HTML/CSS/JS)
- **`GetHealth`** — app health probe for Azure and monitoring (no data exposure)
- **`GetWebAppConfiguration`** — public app config returned at `/configuration.json` (never put secrets or internal hostnames here)

### What Goes in `webApp` Configuration

`webApp` config is served unauthenticated at `/configuration.json`. **Never include:**
- Secrets or API keys
- Internal hostnames or infrastructure details
- Tenant-specific information beyond what the SPA already needs to sign in

Safe to include:
- Feature flags for the SPA
- Client IDs (shared across all users anyway)
- Tenant ID
- App name, version, environment

---

## Logging Policy

**Never log:**
- Tokens or token-like values (bearer tokens, API keys, connection strings)
- Personally identifiable information (user names, emails, internal IDs) — only the `oid` (object ID) claim from a denied request

**Do log:**
- Request paths and HTTP methods
- Status codes
- User's `oid` on 403 (permission denied), so admins can debug role assignment
- Application exceptions and stack traces (in production, errors should be generic; detailed traces go to Application Insights)

---

## Database Access Control

### Gates on Database Initialization

Two gates exist to prevent accidental data loss:

1. **Initialization gate** — An app can only initialize the database if the database is empty
2. **Idempotency gate** — Migrations track which scripts have run; running the same script twice is silent

Together, these mean:
- A production database cannot be accidentally wiped
- A migration can safely re-run without double-applying changes
- A dev environment can be reset by deleting the database and re-running initialization

---

## Shared-Audience Authentication Trade-off

All apps share a single token audience, meaning **separation rests entirely on the role check.** A token issued for `recipes.user` is cryptographically valid for `stockquotes.user`; only the role validation stops it.

**This is acceptable at side-project scale** (low sensitivity data, small team). **It would not be acceptable if:**
- Apps had meaningfully different data sensitivity
- Users needed true per-app isolation
- Regulatory compliance required strong separation

If a future app needs isolation, the solution is separate app registrations (each with its own audience), not a shared one with extra role checks.

---

## Infrastructure Security

- **No secrets in bicep** — Azure Managed Identity provides zero-trust authentication
- **No secrets in source** — configuration comes from Function App settings
- **Firewall rules per-deployment** — SQL Server firewall rules are unique per GitHub Actions run (`gh-<run-id>`) and removed in a finally block, preventing concurrent deploys from fighting over access
- **HTTPS only** — Function Apps require HTTPS (`httpsOnly: true` in bicep)
- **Minimum TLS version** — TLS 1.2 enforced (`minTlsVersion: '1.2'` in bicep)

---

## API Security

**See `docs/auth-setup.md` for the full authentication model and troubleshooting guide.**

Key points:
- App Service Easy Auth must stay disabled (the platform authenticates in-process)
- CORS headers are set **before** authentication, so 401 responses are not hidden behind opaque CORS errors
- `[Authorize]` attributes on endpoints are verified in middleware, not only on function shims
- Role validation is explicit in configuration, not inferred from token structure

---

## Dependency Security

The platform uses:
- `Microsoft.Azure.Functions.Worker` — official Azure Functions runtime
- `Microsoft.Data.SqlClient` — official SQL client with token support
- `Azure.Identity` — official Azure authentication library (supports Managed Identity)
- `EntityFramework Core` — standard ORM with parameterized queries (SQL injection safe)

All dependencies are pinned in `Directory.Packages.props` and reviewed before upgrade.

---

## Code Review Checklist for Security

When reviewing code or PRs:

- [ ] No secrets in code, config files, or comments
- [ ] New endpoints are `[Authorize]` by default; `[AllowAnonymous]` has a comment explaining why
- [ ] No `tenantId == clientId` in configuration
- [ ] Logging does not include tokens or PII (except `oid` on 403)
- [ ] SQL queries use parameterized commands, not string interpolation
- [ ] `UsePlatform()` is called in `Program.cs` (sets up auth middleware)
- [ ] No `IConfiguration.GetValue` or `IConfiguration["key"]` for secrets (use strongly-typed `IOptions<T>`)
