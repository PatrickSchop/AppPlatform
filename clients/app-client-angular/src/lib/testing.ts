import { TenantSession, type MyTenantsResponse } from '@PS/app-client';
import { vi } from 'vitest';

export const TENANT_A = { tenantId: 'aaaaaaaa-0000-0000-0000-000000000001', name: 'Acme', roles: ['editor'] };
export const TENANT_B = { tenantId: 'bbbbbbbb-0000-0000-0000-000000000002', name: 'Globex', roles: ['viewer'] };

export function memoryStorage() {
  const data = new Map<string, string>();
  return {
    getItem: (k: string) => data.get(k) ?? null,
    setItem: (k: string, v: string) => void data.set(k, v),
    removeItem: (k: string) => void data.delete(k),
  };
}

/** A real TenantSession over a fake API, so specs exercise the real state machine. */
export function fakeTenantSession(response: MyTenantsResponse | Error) {
  const get = vi.fn(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
  const session = new TenantSession({
    api: { get } as any,
    header: 'X-Tenant-Id',
    storageKey: 'ps-tenant:spec',
    storage: memoryStorage(),
  });
  return { session, get };
}

export const registered = (...tenants: Array<typeof TENANT_A>): MyTenantsResponse =>
  ({ registered: true, displayName: 'P', tenants });
