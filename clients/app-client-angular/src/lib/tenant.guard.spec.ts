import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { TENANT_SESSION } from './injection-tokens';
import { tenantGuard } from './tenant.guard';
import { fakeTenantSession, registered, TENANT_A, TENANT_B } from './testing';

async function run(session: ReturnType<typeof fakeTenantSession>['session'] | null, url = '/projects?x=1') {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: TENANT_SESSION, useValue: session }],
  });
  const result = await TestBed.runInInjectionContext(() => tenantGuard({} as any, { url } as any));
  return { result, router: TestBed.inject(Router) };
}

describe('tenantGuard', () => {
  it('lets everything through when tenancy is off', async () => {
    expect((await run(null)).result).toBe(true);
  });

  it('lets the user through once a tenant is selected', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A));
    expect((await run(session)).result).toBe(true);
  });

  it('sends a user with several tenants to the picker, remembering where they were going', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A, TENANT_B));

    const { result, router } = await run(session, '/projects?x=1');

    expect(result).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(result as UrlTree)).toBe('/select-tenant?returnUrl=%2Fprojects%3Fx%3D1');
  });

  it('sends a user who is not registered to /not-registered', async () => {
    const { session } = fakeTenantSession({ registered: false, displayName: null, tenants: [] });

    const { result, router } = await run(session);

    expect(router.serializeUrl(result as UrlTree)).toBe('/not-registered');
  });

  it('lets a failed lookup surface as an error instead of denying access silently', async () => {
    const { session } = fakeTenantSession(new Error('offline'));

    await expect(run(session)).rejects.toThrow('offline');
  });

  it('does not look the tenants up again on every navigation', async () => {
    const { session, get } = fakeTenantSession(registered(TENANT_A));
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: TENANT_SESSION, useValue: session }],
    });

    await TestBed.runInInjectionContext(() => tenantGuard({} as any, { url: '/a' } as any));
    await TestBed.runInInjectionContext(() => tenantGuard({} as any, { url: '/b' } as any));

    expect(get).toHaveBeenCalledTimes(1);
  });
});
