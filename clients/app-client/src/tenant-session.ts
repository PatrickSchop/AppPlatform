import type { ApiClient } from './api-client.js';

export interface TenantInfo {
  tenantId: string;
  name: string;
  roles: string[];
}

/** Response of GET me/tenants. */
export interface MyTenantsResponse {
  registered: boolean;
  displayName: string | null;
  tenants: TenantInfo[];
}

export type TenantState =
  | { status: 'idle' }                                  // not signed in / not loaded
  | { status: 'loading' }
  | { status: 'not-registered'; displayName: string | null }
  | { status: 'selecting'; tenants: TenantInfo[] }
  | { status: 'selected'; tenant: TenantInfo; tenants: TenantInfo[] }
  | { status: 'error'; error: unknown };

export interface TenantSessionOptions {
  api: ApiClient;
  /** Request header that carries the tenant id (configuration.json tenancy.header). */
  header: string;
  /** Where the choice is remembered. Make it specific to the app and the signed-in account. */
  storageKey: string;
  /** Defaults to localStorage when it is available. */
  storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;
}

/**
 * The framework-free half of the login flow after sign-in: find the tenants of the signed-in
 * user, pick one automatically or ask, remember the choice, send it on every request and
 * recover when the server rejects it. UI adapters only render {@link TenantSession.state}.
 *
 * Dispatches a `change` event whenever the state changes.
 */
export class TenantSession extends EventTarget {
  private readonly api: ApiClient;
  private readonly header: string;
  private readonly storageKey: string;
  private readonly storage: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'> | null;

  private _state: TenantState = { status: 'idle' };
  private tenants: TenantInfo[] = [];
  private loading: Promise<TenantState> | null = null;

  constructor(options: TenantSessionOptions) {
    super();
    this.api = options.api;
    this.header = options.header;
    this.storageKey = options.storageKey;
    this.storage = options.storage ?? defaultStorage();
  }

  get state(): TenantState {
    return this._state;
  }

  /** The selected tenant's id, or null while there is no selection. */
  get currentTenantId(): string | null {
    return this._state.status === 'selected' ? this._state.tenant.tenantId : null;
  }

  /** Loads the user's tenants and settles on a state. Calls made while loading share one request. */
  load(): Promise<TenantState> {
    if (!this.loading) {
      this.loading = this.doLoad().finally(() => {
        this.loading = null;
      });
    }
    return this.loading;
  }

  /** Chooses one of the loaded tenants and remembers it. Throws when it is not one of them. */
  select(tenantId: string): void {
    const tenant = this.tenants.find(t => t.tenantId === tenantId);
    if (!tenant) {
      throw new Error(`Unknown tenant: ${tenantId}`);
    }
    this.write(tenantId);
    this.setState({ status: 'selected', tenant, tenants: this.tenants });
  }

  /**
   * Forgets the remembered choice and starts again with the tenants already loaded: selected
   * when there is only one, otherwise the picker.
   */
  reset(): void {
    this.forget();
    if (this.tenants.length === 0) {
      return;
    }
    this.settle();
  }

  /** Whether the user has the role in the selected tenant. */
  hasRole(role: string): boolean {
    if (this._state.status !== 'selected') return false;
    return this._state.tenant.roles.some(r => r.toLowerCase() === role.toLowerCase());
  }

  /** Request headers for the selected tenant; empty while there is none. */
  headers(): Record<string, string> {
    const id = this.currentTenantId;
    return id ? { [this.header]: id } : {};
  }

  subscribe(listener: (state: TenantState) => void): () => void {
    const handler = () => listener(this._state);
    this.addEventListener('change', handler);
    return () => this.removeEventListener('change', handler);
  }

  private async doLoad(): Promise<TenantState> {
    this.setState({ status: 'loading' });
    try {
      const response = await this.api.get<MyTenantsResponse>('me/tenants');
      if (!response.registered || response.tenants.length === 0) {
        this.tenants = [];
        this.forget();
        this.setState({ status: 'not-registered', displayName: response.displayName ?? null });
      } else {
        this.tenants = response.tenants;
        this.settle();
      }
    } catch (error) {
      this.setState({ status: 'error', error });
    }
    return this._state;
  }

  /** Picks the state from the loaded tenants and the remembered choice. */
  private settle(): void {
    if (this.tenants.length === 1) {
      this.write(this.tenants[0].tenantId);
      this.setState({ status: 'selected', tenant: this.tenants[0], tenants: this.tenants });
      return;
    }
    const stored = this.read();
    const remembered = stored ? this.tenants.find(t => t.tenantId === stored) : undefined;
    if (remembered) {
      this.setState({ status: 'selected', tenant: remembered, tenants: this.tenants });
      return;
    }
    if (stored) {
      this.forget(); // a stale choice: the user no longer belongs to that tenant
    }
    this.setState({ status: 'selecting', tenants: this.tenants });
  }

  private setState(state: TenantState): void {
    this._state = state;
    this.dispatchEvent(new Event('change'));
  }

  private read(): string | null {
    try {
      return this.storage?.getItem(this.storageKey) ?? null;
    } catch {
      return null;
    }
  }

  private write(tenantId: string): void {
    try {
      this.storage?.setItem(this.storageKey, tenantId);
    } catch {
      // storage unavailable (private mode, quota): the choice just is not remembered
    }
  }

  private forget(): void {
    try {
      this.storage?.removeItem(this.storageKey);
    } catch {
      // see write()
    }
  }
}

function defaultStorage(): Pick<Storage, 'getItem' | 'setItem' | 'removeItem'> | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null; // access can throw when site data is blocked
  }
}
