import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { TENANT_SESSION } from './injection-tokens';
import { TenantService } from './tenant.service';
import { fakeTenantSession, registered, TENANT_A, TENANT_B } from './testing';

function service(session: ReturnType<typeof fakeTenantSession>['session'] | null) {
  TestBed.configureTestingModule({ providers: [{ provide: TENANT_SESSION, useValue: session }] });
  return TestBed.inject(TenantService);
}

describe('TenantService', () => {
  it('is disabled and inert when tenancy is off', async () => {
    const tenants = service(null);

    expect(tenants.enabled).toBe(false);
    expect(await tenants.ready()).toEqual({ status: 'idle' });
    expect(tenants.current()).toBeNull();
    expect(tenants.tenants()).toEqual([]);
    expect(tenants.hasRole('admin')).toBe(false);
    expect(() => tenants.select('x')).toThrow();
  });

  it('is disabled when the token is not provided at all', () => {
    TestBed.configureTestingModule({});

    expect(TestBed.inject(TenantService).enabled).toBe(false);
  });

  it('loads the tenants when nobody has yet, and exposes them as signals', async () => {
    const { session, get } = fakeTenantSession(registered(TENANT_A, TENANT_B));
    const tenants = service(session);
    expect(tenants.enabled).toBe(true);

    const state = await tenants.ready();

    expect(state.status).toBe('selecting');
    expect(tenants.state().status).toBe('selecting');
    expect(tenants.tenants()).toEqual([TENANT_A, TENANT_B]);
    expect(tenants.current()).toBeNull();
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('does not load again when the tenants are already known', async () => {
    const { session, get } = fakeTenantSession(registered(TENANT_A, TENANT_B));
    const tenants = service(session);
    await session.load();

    await tenants.ready();
    await tenants.ready();

    expect(get).toHaveBeenCalledTimes(1);
  });

  it('follows the session: select, role checks and reset', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A, TENANT_B));
    const tenants = service(session);
    await tenants.ready();

    tenants.select(TENANT_B.tenantId);
    expect(tenants.current()).toEqual(TENANT_B);
    expect(tenants.hasRole('viewer')).toBe(true);
    expect(tenants.hasRole('editor')).toBe(false);

    tenants.reset();
    expect(tenants.current()).toBeNull();
    expect(tenants.state().status).toBe('selecting');
  });

  it('shows a user who is not registered', async () => {
    const { session } = fakeTenantSession({ registered: false, displayName: null, tenants: [] });
    const tenants = service(session);

    expect((await tenants.ready()).status).toBe('not-registered');
    expect(tenants.tenants()).toEqual([]);
  });
});
