# Step 27 â€” React starter

**Phase:** 5 â€” Front-end
**Depends on:** Step 26
**Working directory:** `C:\Dev\AppPlatform\starters\react`

## Goal

A Vite + React + TypeScript starter with feature parity to the Angular one, built against
**the same unmodified backend**.

## The rule for this step

**No backend change is permitted.** Not one line in `src/`, `samples/SampleApp` or the shim
package. If something appears to require one, stop and record it â€” it means the Â§4 claim
("the concern does not apply") was wrong somewhere, and that finding is more valuable than
the workaround.

The only legitimate exception is adding a redirect URI to the Entra SPA registration for
`http://localhost:5173`, which is configuration, not code.

## Tasks

### 1. Scaffold

```powershell
cd C:\Dev\AppPlatform\starters
npm create vite@latest react -- --template react-ts
cd react
npm install
npm i @PS/app-client @PS/app-client-react @azure/msal-browser react-router-dom
```

### 2. `main.tsx`

```tsx
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <PlatformProvider fallback={<Splash />} errorFallback={(e, retry) => <StartupError error={e} onRetry={retry} />}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </PlatformProvider>
  </StrictMode>
);
```

Keep `StrictMode` on. It is the environment Step 25's double-initialisation guard exists for,
and turning it off to make a warning go away would hide a real leak.

### 3. Routes

Mirror the Angular starter so Gate C is a like-for-like comparison:
- `/` â€” home, showing config and auth state
- `/example` â€” a list page, and the deep-link test target
- `/403` â€” the role-aware forbidden page

`react-router-dom` with `BrowserRouter`. This is the case that made the Â§4 backend fix
necessary in the first place: without the extensionless fallback from Step 09, a hard refresh
on `/example` returns 404 and the app is unusable.

### 4. Styling

Port the same design tokens from `_variables.scss` so the two starters are visibly siblings,
but keep the React one lighter â€” plain CSS modules or a single stylesheet, no Bootstrap.

That difference is deliberate and worth stating in the README: the Angular starter inherits an
existing Bootstrap-based system; a new React app has no such history and should not acquire
one for free.

### 5. Components

- `<Navbar>` â€” title from `useConfig()`, sign-in/sign-out from `useAuth()`, and `<TaskProgress />`
- `<NotesList>` â€” `useApiQuery<Note[]>('/notes')` with a create form
- `<StartTaskButton>` â€” `useBackgroundTasks().createTask(...)`, then progress appears
- `<RequireAuth>` â€” a route wrapper redirecting to sign-in, and to `/403` when the role is missing

### 6. `public/configuration.json`

Same arrangement as the Angular starter â€” a dev-only file that the deploy workflow deletes.

Vite's dev server serves `public/` at the root, so `/configuration.json` resolves locally
exactly as it does in production. Note that in the README.

### 7. Vite dev proxy

```ts
server: {
  port: 5173,
  proxy: { '/api': 'http://localhost:7071' }
}
```

The proxy sidesteps CORS in development entirely. Keep `httpAccessControl:allowOrigin`
including `http://localhost:5173` anyway, so a developer who runs without the proxy is not
stuck â€” and so the multi-origin support added in Step 10 is actually exercised.

### 8. Wire into the template

Add as the `Frontend=react` content for `dotnet new tinyapp` (Step 18), scaffolded to
`WebApp-React/` or `WebApp/` â€” pick one and be consistent with the deploy workflow's
`frontend_path`.

### 9. README

Getting started, the hooks in use, config resolution in dev vs deployed, and the explicit
note that this starter was built with **zero backend changes**.

## Verification

```powershell
cd C:\Dev\AppPlatform\starters\react
npm run build
```

Then against the sample backend:

```powershell
# shell 1
cd C:\Dev\AppPlatform\samples\SampleApp; func start
# shell 2
cd C:\Dev\AppPlatform\starters\react; npm run dev
```

At `http://localhost:5173`:

- [ ] Title from `/configuration.json`
- [ ] Notes load and can be created
- [ ] A background task runs with advancing progress
- [ ] No console errors
- [ ] **No duplicate network requests** under StrictMode â€” the Step 25 guard working

Then the deployed shape:

```powershell
npm run build
# point staticContent:files:rootPath at starters/react/dist, restart func
```

- [ ] `http://localhost:7071/` loads
- [ ] **`http://localhost:7071/example` on a hard refresh loads the app**

## The Gate C evidence

Record in `docs/gate-c-results.md`:
- **the exact backend commit SHA** both starters ran against
- confirmation that the SHA is identical for both, and unchanged since Step 26
- any backend change that turned out to be needed â€” and if there were none, say so plainly

The SHA is the whole proof. "Both front-ends work" is a claim; "both front-ends work against
commit `abc1234`" is evidence.

## Done when

- [ ] Builds clean and runs against the sample backend
- [ ] Feature parity with the Angular starter
- [ ] Deep links work on hard refresh with `react-router-dom`
- [ ] StrictMode produces no duplicate polling
- [ ] **Zero backend changes** â€” verified by git log
- [ ] Wired into the template as `Frontend=react`

## Commit

```powershell
git add -A
git commit -m "Step 27: React starter, built with no backend changes"
```

