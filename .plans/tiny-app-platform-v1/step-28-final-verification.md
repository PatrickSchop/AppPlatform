# Step 28 — Gate C and release

**Phase:** 5 — Front-end
**Depends on:** Step 27
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Prove front-end flexibility is real rather than claimed, tie off the documentation, and
release `1.0.0`.

## Check 1 — Gate C: one backend, two front-ends

The analysis §8 formulation: build the React starter against the same running backend and
confirm background-task progress works with no backend change.

```powershell
cd C:\Dev\AppPlatform
git log --oneline -1 -- src/ samples/
```

Record that SHA. It must predate Step 27 — if a backend commit appears after Step 26, the
"no backend change" claim is false and needs explaining.

Build both front-ends and serve each through the **same** backend build:

```powershell
cd starters\angular; npm run build; cd ..\..
cd starters\react;   npm run build; cd ..\..
```

For each in turn, point `staticContent:files:rootPath` at that starter's `dist`, restart
`func start` **without rebuilding the backend**, and verify:

| Check | Angular | React |
|---|---|---|
| App shell loads at `/` | | |
| Title from `/configuration.json` | | |
| Notes list and create | | |
| Background task progress advances to completion | | |
| Hard refresh on a deep link serves the app | | |
| No console errors | | |

**Gate C passes when both columns are complete and the backend binary was not rebuilt
between them.**

## Check 2 — Full regression

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
Get-ChildItem clients,starters -Directory | ForEach-Object {
    Push-Location $_.FullName
    if (Test-Path package.json) { npm ci; npm run build; npm test --if-present }
    Pop-Location
}
```

**Expected:** everything green.

Re-run **Gate A** (Step 15) in full. Twenty-eight steps of change since it passed is plenty
of room for a regression, and the metadata check in particular is cheap to re-run and
expensive to have wrong.

## Check 3 — The architecture invariants still hold

```powershell
# No [Function] in the engine
Select-String -Path src\Wisdi.AppPlatform\**\*.cs -Pattern '\[Function\('

# No secrets
Select-String -Path src,samples,templates,starters,clients -Pattern 'apiKey"\s*:\s*"[^"]{20,}' -Recurse

# No GUID in template content
Select-String -Path templates\content -Pattern '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}' -Recurse

# No SignalR anywhere
Select-String -Path . -Pattern 'signalr' -Recurse -Exclude *.md
```

**Expected:** all four return nothing.

## Check 4 — The documentation is usable

Read each doc as someone who has not seen the code:

- [ ] `README.md` — what this is, the five deliverables, how to start
- [ ] `docs/getting-started.md` — `dotnet new` through to deployed
- [ ] `docs/consuming-packages.md` — feed auth, including the classic-PAT and clear-text traps
- [ ] `docs/auth-setup.md` — the Entra runbook
- [ ] `docs/provisioning.md` — bicep, provision **and** teardown
- [ ] `docs/deployment.md` — workflows, inputs, the three likely failures
- [ ] `docs/background-tasks.md` — handlers, and the lease obligation
- [ ] `docs/security.md` — secrets policy, the shared-audience trade-off, the key rotation
- [ ] `docs/versioning.md` — SemVer and the two-package lockstep rule
- [ ] `docs/architecture.md` — **write this now if it does not exist**

`docs/architecture.md` should be short and cover only what is not obvious from the code:
why `[Function]` shims are injected source (§2.1), why the DbContext is generic (§2.2), why
CORS runs before auth, why the execution manager id is a singleton, and why migrations are
tracked. These are all decisions someone will otherwise "simplify" back into bugs.

## Check 5 — Close out the analysis findings

Walk `.plans/source-project-analysis.md` §7 and §6 and confirm each is addressed:

| Finding | Where | Done |
|---|---|---|
| §7.1 scoped manager breaks concurrency | Step 07 | |
| §7.2 no orphan recovery | Step 08 | |
| §7.3 only trigger is a self-POST | Step 13 | |
| §7.4 contract drift — `POST /api/tasks` | Step 12 | |
| §7.4 contract drift — `POST /api/analysis/analyze` | n/a — domain, stays in StockAnalysis | |
| §7.5 non-deterministic discovery order | Step 03 — documented as a contract | |
| §7.6 dead `BuildConfiguration` hook | Step 03 — dropped | |
| §6 no enforcement / default-deny | Step 10 | |
| §6 middleware cannot see attributes | Step 10 | |
| §6 CORS ordering | Step 10 | |
| §6 root-provider resolution | Step 10 | |
| §6 `tenantId == clientId` | Step 10 + Step 22 | |
| §6 front-end cannot authenticate | Steps 23, 26 | |
| §6 leaked Cognitive Services key | Step 22 — **rotated** | |
| §4 SPA deep links | Step 09 | |
| §4 content types, ETag, Cache-Control | Step 09 | |
| §3 migration tracking and drift | Step 05 | |
| §5 no infrastructure as code | Step 20 | |
| §5 workflow input bugs | Step 21 | |
| §1 dead SignalR code | Not ported | |

Anything unticked is either a v1.1 item or was deliberately dropped. **Write down which**, in
`docs/v1-release-notes.md`. An unexplained gap in this table is the thing a future reader will
trip over.

## Check 6 — Release 1.0.0

```powershell
cd C:\Dev\AppPlatform
# set VersionPrefix to 1.0.0 in Directory.Build.props
git add -A
git commit -m "Release 1.0.0"
git tag v1.0.0
git push origin main --tags
gh release create v1.0.0 --title "1.0.0" --notes-file docs\v1-release-notes.md
```

Then verify the published packages one last time by scaffolding a throwaway app against
`1.0.0` — `dotnet new tinyapp -n ReleaseCheck --PlatformVersion 1.0.0`, build, confirm
`functions.metadata`. A release that has not been consumed has not been tested.

Finally, update the template's `deploy.yaml` pin from `@v0.1.0` to `@v1.0.0`, and re-release
the templates package.

## Gate C checklist

- [ ] Both starters run against one unmodified backend build, with the SHA recorded
- [ ] Background task progress works in both
- [ ] Deep links work in both
- [ ] Gate A re-run and still passing
- [ ] All four architecture greps return nothing
- [ ] Every doc in Check 4 exists and is usable
- [ ] The §6/§7 findings table is complete, with gaps explained
- [ ] `1.0.0` published and verified by a fresh scaffold

## Record the result

`docs/gate-c-results.md`: the backend SHA, both starter results, and anything that had to
change. Together with `gate-a-results.md` and `gate-b-results.md` this is the evidence that
the platform does what it claims.

## What v1 deliberately does not include

Restate in `docs/v1-release-notes.md`, from the container document §9:

- **Migrating StockAnalysis onto the platform** — the natural next plan, now that the
  packages are published and proven by two independent apps
- **Multi-tenancy** — no `UserId`/`TenantId`, no query filters; the per-app database model
  sidesteps it. If an app needs per-user data, the platform grows a `UserScopedEntity`
  convention rather than each app inventing one
- **Schema parameterisation** of the claim SQL
- Anything else the Gate B friction log surfaced that was not worth holding the release for —
  list it, so v1.1 has a starting point rather than a blank page

## Commit

```powershell
git add -A
git commit -m "Step 28: Gate C passed; 1.0.0 released"
```
