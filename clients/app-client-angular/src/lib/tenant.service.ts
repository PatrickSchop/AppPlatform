import { Injectable, computed, inject, signal } from '@angular/core';
import type { TenantInfo, TenantState } from '@PS/app-client';
import { TENANT_SESSION } from './injection-tokens';

/**
 * The login flow's tenant state as signals. Wraps the framework-free TenantSession: the logic
 * lives there, this only renders it for Angular.
 */
@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly session = inject(TENANT_SESSION, { optional: true }) ?? null;
  private readonly _state = signal<TenantState>(this.session?.state ?? { status: 'idle' });

  /** False when the app does not run in Single or Multi tenancy mode. */
  readonly enabled = this.session !== null;
  readonly state = this._state.asReadonly();
  /** The selected tenant, or null while there is none. */
  readonly current = computed<TenantInfo | null>(() => {
    const state = this._state();
    return state.status === 'selected' ? state.tenant : null;
  });
  /** The tenants the user can choose from; empty until they are loaded. */
  readonly tenants = computed<TenantInfo[]>(() => {
    const state = this._state();
    return state.status === 'selecting' || state.status === 'selected' ? state.tenants : [];
  });

  constructor() {
    this.session?.subscribe(state => this._state.set(state));
  }

  /** Waits for the tenants to be known (loading them when nobody has yet) and returns the state. */
  async ready(): Promise<TenantState> {
    if (!this.session) return { status: 'idle' };
    const state = await this.session.ready();
    return state.status === 'idle' ? this.session.load() : state;
  }

  select(tenantId: string): void {
    this.requireSession().select(tenantId);
  }

  reset(): void {
    this.session?.reset();
  }

  /** Whether the user has the role in the selected tenant. */
  hasRole(role: string): boolean {
    return this.session?.hasRole(role) ?? false;
  }

  private requireSession() {
    if (!this.session) {
      throw new Error('Tenancy is not enabled for this app.');
    }
    return this.session;
  }
}
