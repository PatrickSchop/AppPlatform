import { describe, it, expect, vi } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { ApiClient, ApiError } from './api-client.js';
import { TenantSession, type MyTenantsResponse } from './tenant-session.js';

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

const A = { tenantId: 'aaaaaaaa-0000-0000-0000-000000000001', name: 'Acme', roles: ['editor'] };
const B = { tenantId: 'bbbbbbbb-0000-0000-0000-000000000002', name: 'Globex', roles: ['viewer', 'Reporter'] };
const KEY = 'ps-tenant:test:anon';

function fakeStorage(initial: Record<string, string> = {}) {
  const data = new Map(Object.entries(initial));
  return {
    data,
    getItem: (k: string) => data.get(k) ?? null,
    setItem: (k: string, v: string) => void data.set(k, v),
    removeItem: (k: string) => void data.delete(k),
  };
}

function session(response: MyTenantsResponse | Error, storage = fakeStorage()) {
  const fetchMock = vi.fn(async () => {
    if (response instanceof Error) throw response;
    return json(response);
  });
  const api = new ApiClient({ baseUrl: '/api', fetch: fetchMock as any });
  const tenant = new TenantSession({ api, header: 'X-Tenant-Id', storageKey: KEY, storage });
  return { tenant, storage, fetchMock };
}

const registered = (...tenants: typeof A[]): MyTenantsResponse => ({ registered: true, displayName: 'P', tenants });

describe('TenantSession.load', () => {
  it('selects the only tenant without asking, and remembers it', async () => {
    const { tenant, storage } = session(registered(A));

    const state = await tenant.load();

    expect(state).toEqual({ status: 'selected', tenant: A, tenants: [A] });
    expect(storage.getItem(KEY)).toBe(A.tenantId);
  });

  it('asks when there are several tenants and nothing is remembered; select() remembers and notifies', async () => {
    const { tenant, storage } = session(registered(A, B));
    const changes = vi.fn();
    tenant.addEventListener('change', changes);

    expect((await tenant.load()).status).toBe('selecting');
    changes.mockClear();
    tenant.select(B.tenantId);

    expect(tenant.state).toEqual({ status: 'selected', tenant: B, tenants: [A, B] });
    expect(storage.getItem(KEY)).toBe(B.tenantId);
    expect(changes).toHaveBeenCalledTimes(1);
  });

  it('reuses a remembered tenant that is still valid', async () => {
    const { tenant } = session(registered(A, B), fakeStorage({ [KEY]: B.tenantId }));

    const state = await tenant.load();

    expect(state).toMatchObject({ status: 'selected', tenant: B });
  });

  it('drops a remembered tenant the user no longer belongs to and asks again', async () => {
    const { tenant, storage } = session(registered(A, B), fakeStorage({ [KEY]: 'cccccccc-gone' }));

    const state = await tenant.load();

    expect(state.status).toBe('selecting');
    expect(storage.getItem(KEY)).toBeNull();
  });

  it('reports a user who is not registered', async () => {
    const { tenant } = session({ registered: false, displayName: null, tenants: [] });

    expect(await tenant.load()).toEqual({ status: 'not-registered', displayName: null });
  });

  it('treats registered-without-tenants as not registered', async () => {
    const { tenant } = session({ registered: true, displayName: 'P', tenants: [] });

    expect(await tenant.load()).toEqual({ status: 'not-registered', displayName: 'P' });
  });

  it('reports a failed request as an error state, not an exception', async () => {
    const { tenant } = session(new Error('offline'));

    const state = await tenant.load();

    expect(state.status).toBe('error');
  });

  it('shares one request between concurrent callers', async () => {
    const { tenant, fetchMock } = session(registered(A, B));

    await Promise.all([tenant.load(), tenant.load()]);

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('ready() waits for a load in flight and never starts one', async () => {
    const { tenant, fetchMock } = session(registered(A, B));

    expect(await tenant.ready()).toEqual({ status: 'idle' });
    expect(fetchMock).not.toHaveBeenCalled();

    const loading = tenant.load();
    const ready = tenant.ready();
    await loading;

    expect((await ready).status).toBe('selecting');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('asks for me/tenants relative to the API root', async () => {
    const { tenant, fetchMock } = session(registered(A));

    await tenant.load();

    expect(fetchMock.mock.calls[0][0]).toBe('/api/me/tenants');
  });
});

describe('TenantSession selection', () => {
  it('refuses a tenant the user does not belong to', async () => {
    const { tenant } = session(registered(A, B));
    await tenant.load();

    expect(() => tenant.select('nope')).toThrow();
    expect(tenant.state.status).toBe('selecting');
  });

  it('sends the tenant header only once a tenant is selected', async () => {
    const { tenant } = session(registered(A, B));
    expect(tenant.headers()).toEqual({});

    await tenant.load();
    expect(tenant.headers()).toEqual({});

    tenant.select(B.tenantId);
    expect(tenant.headers()).toEqual({ 'X-Tenant-Id': B.tenantId });
    expect(tenant.currentTenantId).toBe(B.tenantId);
  });

  it('reset() forgets the choice and asks again when there are several tenants', async () => {
    const { tenant, storage } = session(registered(A, B), fakeStorage({ [KEY]: B.tenantId }));
    await tenant.load();

    tenant.reset();

    expect(tenant.state.status).toBe('selecting');
    expect(storage.getItem(KEY)).toBeNull();
    expect(tenant.headers()).toEqual({});
  });

  it('reset() with a single tenant selects it again', async () => {
    const { tenant } = session(registered(A));
    await tenant.load();

    tenant.reset();

    expect(tenant.state).toMatchObject({ status: 'selected', tenant: A });
  });

  it('hasRole() looks at the selected tenant, ignoring case', async () => {
    const { tenant } = session(registered(A, B));
    await tenant.load();
    expect(tenant.hasRole('viewer')).toBe(false);

    tenant.select(B.tenantId);

    expect(tenant.hasRole('viewer')).toBe(true);
    expect(tenant.hasRole('reporter')).toBe(true);
    expect(tenant.hasRole('editor')).toBe(false);
  });

  it('subscribe() reports state changes until it is unsubscribed', async () => {
    const { tenant } = session(registered(A, B));
    const seen: string[] = [];
    const unsubscribe = tenant.subscribe(s => seen.push(s.status));

    await tenant.load();
    unsubscribe();
    tenant.select(A.tenantId);

    expect(seen).toEqual(['loading', 'selecting']);
  });

  it('still works when storage is unavailable', async () => {
    const throwing = {
      getItem: () => { throw new Error('blocked'); },
      setItem: () => { throw new Error('blocked'); },
      removeItem: () => { throw new Error('blocked'); },
    };
    const api = new ApiClient({ baseUrl: '/api', fetch: (async () => json(registered(A, B))) as any });
    const tenant = new TenantSession({ api, header: 'X-Tenant-Id', storageKey: KEY, storage: throwing });

    await tenant.load();
    tenant.select(B.tenantId);

    expect(tenant.state).toMatchObject({ status: 'selected', tenant: B });
  });
});

describe('ApiClient tenant hooks', () => {
  it('merges getHeaders() into every request', async () => {
    const fetchMock = vi.fn(async () => json({}));
    const api = new ApiClient({
      baseUrl: '/api',
      fetch: fetchMock as any,
      getHeaders: () => ({ 'X-Tenant-Id': 'T1' }),
    });

    await api.get('projects');

    const headers: Headers = (fetchMock.mock.calls[0] as any)[1].headers;
    expect(headers.get('X-Tenant-Id')).toBe('T1');
  });

  it('calls onErrorCode with the status and code before throwing', async () => {
    const order: string[] = [];
    const fetchMock = vi.fn(async () => json({ error: 'tenant_forbidden' }, 403));
    const api = new ApiClient({
      baseUrl: '/api',
      fetch: fetchMock as any,
      onErrorCode: (status, code) => order.push(`${status}:${code}`),
    });

    await api.get('projects').catch(e => order.push(e instanceof ApiError ? 'thrown' : 'other'));

    expect(order).toEqual(['403:tenant_forbidden', 'thrown']);
  });

  it('does not call onErrorCode for a body without an error code', async () => {
    const onErrorCode = vi.fn();
    const api = new ApiClient({
      baseUrl: '/api',
      fetch: (async () => new Response('Bad gateway', { status: 502 })) as any,
      onErrorCode,
    });

    await api.get('x').catch(() => {});

    expect(onErrorCode).not.toHaveBeenCalled();
  });
});

describe('package', () => {
  it('has no runtime dependencies', () => {
    const pkg = JSON.parse(readFileSync(resolve(process.cwd(), 'package.json'), 'utf-8'));
    expect(Object.keys(pkg.dependencies ?? {})).toEqual([]);
  });
});
