# Step MT-14 — Angular and React tenancy components; starters

**Phase:** 3 — Front-end and template
**Depends on:** MT-13
**Working directory:** `C:\Dev\AppPlatform\clients` and `C:\Dev\AppPlatform\starters`

## Goal

Make the standard login flow a few lines in either framework: a guard or gate, a picker, a
switcher. Both starters then work unchanged in `None` mode and show the flow in
`Single`/`Multi` mode, decided at runtime from `/configuration.json`.

## Tasks

### 1. `@PS/app-client-angular`

```ts
export function provideTenancy(): EnvironmentProviders;  // no-op when tenant is null

@Injectable({ providedIn: 'root' })
export class TenantService {
  readonly state: Signal<TenantState>;          // from TenantSession via subscribe
  readonly current: Signal<TenantInfo | null>;
  readonly enabled: boolean;                    // false in None mode
  select(tenantId: string): void;
  reset(): void;
  hasRole(role: string): boolean;
}

export const tenantGuard: CanActivateFn;
// None mode or selected → true
// selecting             → UrlTree('/select-tenant', { queryParams: { returnUrl } })
// not-registered        → UrlTree('/not-registered')
// awaits load() first; loading shows nothing (no flicker)

@Component({ selector: 'ps-tenant-picker', standalone: true, ... })
export class TenantPickerComponent {}   // list of tenants (name + roles), select → returnUrl

@Component({ selector: 'ps-tenant-switcher', standalone: true, ... })
export class TenantSwitcherComponent {} // header dropdown; hidden unless tenants.length > 1

@Component({ selector: 'ps-not-registered', standalone: true, ... })
export class NotRegisteredComponent {}  // account name, "ask an administrator", sign-out
```

The components use the starter's design-system classes, not their own styling, and they are
overridable by content projection or by apps simply not using them.

### 2. `@PS/app-client-react`

```tsx
export function TenantProvider(props: { session: TenantSession | null; children: ReactNode }): JSX.Element;
export function useTenant(): {
  state: TenantState; current: TenantInfo | null; enabled: boolean;
  select(id: string): void; reset(): void; hasRole(role: string): boolean;
};                                      // useSyncExternalStore over session.subscribe

export function TenantGate(props: {
  children: ReactNode;
  picker?: ReactNode; notRegistered?: ReactNode; loading?: ReactNode;
}): JSX.Element;                        // renders children only when selected (or None mode)

export function TenantPicker(): JSX.Element;
export function TenantSwitcher(): JSX.Element;   // hidden unless > 1 tenant
```

### 3. Starters

**Angular** (`starters/angular`): add routes `select-tenant` → `TenantPickerComponent` and
`not-registered` → `NotRegisteredComponent`, put `tenantGuard` on the authenticated route
group next to the existing auth guard (auth first), and put `<ps-tenant-switcher>` in the shell
header.

**React** (`starters/react`): wrap the authenticated tree in `<TenantGate>` and add
`<TenantSwitcher>` to the header.

In `None` mode both starters behave exactly as at v1 Gate C: the guard or gate is a
pass-through, and the switcher renders nothing.

### 4. Management UI alignment

Replace MT-11's hand-written `GET /api/me/tenants` gate with `TenantService`
(`hasRole('admin')`). The management app is `Single`, so the picker never shows. This removes
the duplicate call and proves the components work in `Single` mode.

## Tests to add

Angular (Jest/Karma, whichever the starter uses):
1. `tenantGuard`: `None` → true; `selecting` → `/select-tenant?returnUrl=…`;
   `not-registered` → `/not-registered`; `selected` → true.
2. Picker click → `select()` then navigation to `returnUrl`.
3. Switcher hidden with one tenant, visible with two; choosing a tenant calls `select`.

React (vitest + Testing Library):
4. `TenantGate` renders the picker, not-registered or children per state.
5. `useTenant` re-renders on session change.
6. Switcher visibility as in 3.

## Verification

```powershell
cd C:\Dev\AppPlatform\clients\app-client-angular; npm run build; npm test
cd ..\app-client-react; npm run build; npm test
cd ..\..\starters\angular; npm run build; npm test
cd ..\react; npm run build; npm test
```

Manual, against `samples/MultiTenantSample` (Gate D configuration: you are in Contoso and
Fabrikam), for **each** starter pointed at it:
1. Sign in → the picker shows Contoso and Fabrikam.
2. Choose Contoso → the app loads; the switcher shows Contoso.
3. Reload → no picker (the choice is remembered).
4. Switch to Fabrikam → data reloads; background-task list changes.
5. Remove Contoso from `devDirectory`, restart the host, reload → the stored choice is invalid,
   so you land on the picker (or auto-select Fabrikam, the only one left).

And against `SampleApp` (`None`): v1 Gate C still passes, with no picker and no extra requests.

## Done when

- [ ] Each framework needs one provider/gate, one route or wrapper, and one header component
- [ ] Both starters pass Gate C unchanged in `None` mode
- [ ] The manual flow above passes for both starters
- [ ] The management UI uses the shared components

## Commit

```powershell
git add -A
git commit -m "MT-14: Angular and React tenancy components, starters support tenant selection"
```
