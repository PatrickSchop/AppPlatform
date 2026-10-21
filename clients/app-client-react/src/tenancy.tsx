import {
  createContext,
  ReactNode,
  useCallback,
  useContext,
  useEffect,
  useSyncExternalStore,
  JSX,
} from 'react';
import type { TenantInfo, TenantSession, TenantState } from '@PS/app-client';

const TenantContext = createContext<TenantSession | null>(null);

/** Supplies the tenant session to the tenancy hooks and components. `null` means tenancy is off. */
export function TenantProvider(props: {
  session: TenantSession | null;
  children: ReactNode;
}): JSX.Element {
  return <TenantContext.Provider value={props.session}>{props.children}</TenantContext.Provider>;
}

const IDLE: TenantState = { status: 'idle' };
const NO_SUBSCRIPTION = () => () => {};

export interface UseTenant {
  state: TenantState;
  /** The selected tenant, or null while there is none. */
  current: TenantInfo | null;
  /** False when the app does not run in Single or Multi tenancy mode. */
  enabled: boolean;
  select(tenantId: string): void;
  reset(): void;
  /** Whether the user has the role in the selected tenant. */
  hasRole(role: string): boolean;
}

export function useTenant(): UseTenant {
  const session = useContext(TenantContext);

  const subscribe = useCallback(
    (listener: () => void) => (session ? session.subscribe(listener) : NO_SUBSCRIPTION()),
    [session]
  );
  const getSnapshot = useCallback(() => (session ? session.state : IDLE), [session]);
  const state = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);

  return {
    state,
    current: state.status === 'selected' ? state.tenant : null,
    enabled: session !== null,
    select: (tenantId: string) => {
      if (!session) throw new Error('Tenancy is not enabled for this app.');
      session.select(tenantId);
    },
    reset: () => session?.reset(),
    hasRole: (role: string) => session?.hasRole(role) ?? false,
  };
}

function tenantsOf(state: TenantState): TenantInfo[] {
  return state.status === 'selecting' || state.status === 'selected' ? state.tenants : [];
}

/** Lists the user's tenants; choosing one remembers it. */
export function TenantPicker(): JSX.Element {
  const { state, select } = useTenant();

  return (
    <section className="ps-tenant-picker">
      <h1 className="ps-tenant-picker-title">Choose a tenant</h1>
      <ul className="ps-tenant-list">
        {tenantsOf(state).map(tenant => (
          <li key={tenant.tenantId}>
            <button type="button" className="ps-tenant-option" onClick={() => select(tenant.tenantId)}>
              <span className="ps-tenant-name">{tenant.name}</span>
              {tenant.roles.length > 0 && (
                <span className="ps-tenant-roles">{tenant.roles.join(', ')}</span>
              )}
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

/**
 * A drop-down to change tenant, for the app's header. Renders nothing unless there is more than one.
 * After a switch the page reloads by default, so nothing shown for the previous tenant stays on
 * screen; pass `onSwitched` to reload only the data instead.
 */
export function TenantSwitcher(props: { onSwitched?: () => void }): JSX.Element | null {
  const { state, current, select } = useTenant();
  const tenants = tenantsOf(state);
  if (tenants.length <= 1) return null;

  return (
    <label className="ps-tenant-switcher">
      <span className="ps-tenant-switcher-label">Tenant</span>
      <select
        className="ps-tenant-switcher-select"
        value={current?.tenantId ?? ''}
        onChange={event => {
          if (event.target.value === current?.tenantId) return;
          select(event.target.value);
          (props.onSwitched ?? (() => window.location.reload()))();
        }}
      >
        {tenants.map(tenant => (
          <option key={tenant.tenantId} value={tenant.tenantId}>
            {tenant.name}
          </option>
        ))}
      </select>
    </label>
  );
}

function DefaultNotRegistered(): JSX.Element {
  return (
    <section className="ps-not-registered">
      <h1 className="ps-not-registered-title">You do not have access</h1>
      <p className="ps-not-registered-help">Ask an administrator to invite you.</p>
    </section>
  );
}

/**
 * Renders its children only once a tenant is selected. Before that it shows the picker, the
 * not-registered page, a loading placeholder or an error, each replaceable. Passes straight through
 * when tenancy is off.
 */
export function TenantGate(props: {
  children: ReactNode;
  picker?: ReactNode;
  notRegistered?: ReactNode;
  loading?: ReactNode;
  error?: (error: unknown, retry: () => void) => ReactNode;
}): JSX.Element {
  const session = useContext(TenantContext);
  const { state, enabled } = useTenant();

  // The lookup normally starts with the platform client; this covers a session nobody has loaded.
  useEffect(() => {
    if (session && session.state.status === 'idle') {
      void session.load();
    }
  }, [session]);

  if (!enabled) return <>{props.children}</>;

  switch (state.status) {
    case 'selected':
      return <>{props.children}</>;
    case 'selecting':
      return <>{props.picker ?? <TenantPicker />}</>;
    case 'not-registered':
      return <>{props.notRegistered ?? <DefaultNotRegistered />}</>;
    case 'error':
      return (
        <>
          {props.error ? (
            props.error(state.error, () => void session?.load())
          ) : (
            <section className="ps-tenant-error">
              <p>The tenants could not be loaded.</p>
              <button type="button" onClick={() => void session?.load()}>
                Try again
              </button>
            </section>
          )}
        </>
      );
    default:
      return <>{props.loading ?? null}</>;
  }
}
