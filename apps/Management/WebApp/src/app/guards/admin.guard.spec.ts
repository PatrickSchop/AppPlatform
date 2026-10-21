import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { describe, it, expect, vi } from 'vitest';
import { TenantSession, type MyTenantsResponse } from '@PS/app-client';
import { AUTH_CLIENT, TENANT_SESSION } from '@PS/app-client-angular';
import { adminGuard } from './admin.guard';

const TENANT = { tenantId: 'tenant-1', name: 'Default', roles: [] as string[] };

function storage() {
  const data = new Map<string, string>();
  return {
    getItem: (k: string) => data.get(k) ?? null,
    setItem: (k: string, v: string) => void data.set(k, v),
    removeItem: (k: string) => void data.delete(k),
  };
}

function session(response: MyTenantsResponse | Error) {
  const get = vi.fn(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
  return {
    get,
    session: new TenantSession({ api: { get } as any, header: 'X-Tenant-Id', storageKey: 'spec', storage: storage() }),
  };
}

const registered = (roles: string[]): MyTenantsResponse => ({
  registered: true,
  displayName: 'P',
  tenants: [{ ...TENANT, roles }],
});

interface Setup {
  tenant?: ReturnType<typeof session>['session'] | null;
  auth?: unknown;
}

async function run({ tenant = null, auth = { getAccount: () => ({ username: 'p' }), getError: () => null, signIn: vi.fn() } }: Setup) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AUTH_CLIENT, useValue: auth },
      { provide: TENANT_SESSION, useValue: tenant },
    ],
  });
  const result = await TestBed.runInInjectionContext(() => adminGuard({} as any, {} as any));
  return { result, router: TestBed.inject(Router) };
}

const where = (r: { result: unknown; router: Router }) => r.router.serializeUrl(r.result as UrlTree);

describe('adminGuard', () => {
  it('lets an administrator in', async () => {
    const { session: tenant } = session(registered(['admin']));

    expect((await run({ tenant })).result).toBe(true);
  });

  it('sends a registered user without the admin role to /no-access', async () => {
    const { session: tenant } = session(registered([]));

    expect(where(await run({ tenant }))).toBe('/no-access');
  });

  it('does not accept a different role as admin', async () => {
    const { session: tenant } = session(registered(['viewer']));

    expect(where(await run({ tenant }))).toBe('/no-access');
  });

  it('sends an unregistered user to /no-access', async () => {
    const { session: tenant } = session({ registered: false, displayName: null, tenants: [] });

    expect(where(await run({ tenant }))).toBe('/no-access');
  });

  it('sends a lookup failure to the error page, not to /no-access', async () => {
    const failure = Object.assign(new Error('API error 500'), { status: 500 });
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const { session: tenant } = session(failure);

    const url = where(await run({ tenant }));

    expect(url).toBe('/error?message=The%20management%20API%20returned%20HTTP%20500.');
  });

  it('asks an anonymous visitor to sign in instead of looking anything up', async () => {
    const auth = { getAccount: () => null, getError: () => null, signIn: vi.fn() };
    const { session: tenant, get } = session(registered(['admin']));

    const { result } = await run({ tenant, auth });

    expect(result).toBe(false);
    expect(auth.signIn).toHaveBeenCalled();
    expect(get).not.toHaveBeenCalled();
  });

  it('shows a failed sign-in instead of retrying it', async () => {
    const auth = { getAccount: () => null, getError: () => 'AADSTS500011', signIn: vi.fn() };
    const { session: tenant } = session(registered(['admin']));

    const result = await run({ tenant, auth });

    expect(where(result)).toBe('/no-access');
    expect(auth.signIn).not.toHaveBeenCalled();
  });

  it('denies access when tenancy or auth is not configured', async () => {
    expect(where(await run({ tenant: null }))).toBe('/no-access');
  });

  it('does not look the tenants up again for a second navigation', async () => {
    const { session: tenant, get } = session(registered(['admin']));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AUTH_CLIENT, useValue: { getAccount: () => ({ username: 'p' }), getError: () => null } },
        { provide: TENANT_SESSION, useValue: tenant },
      ],
    });

    await TestBed.runInInjectionContext(() => adminGuard({} as any, {} as any));
    await TestBed.runInInjectionContext(() => adminGuard({} as any, {} as any));

    expect(get).toHaveBeenCalledTimes(1);
  });
});
