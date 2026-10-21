import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { resetConfigCache } from './config.js';
import { createPlatformClient } from './index.js';

// The identity-provider library is replaced so the account (and its id) is under the test's control.
const idp = vi.hoisted(() => ({ homeAccountId: 'acct-1' }));
vi.mock('@azure/msal-browser', () => {
  class PublicClientApplication {
    async initialize() {}
    async handleRedirectPromise() { return null; }
    getActiveAccount() { return { username: 'q@example.test', name: 'Q', homeAccountId: idp.homeAccountId }; }
    getAllAccounts() { return [this.getActiveAccount()]; }
    setActiveAccount() {}
    async acquireTokenSilent() { return { accessToken: 'token' }; }
    async clearCache() {}
    async loginRedirect() {}
    async logout() {}
  }
  class InteractionRequiredAuthError extends Error {}
  return { PublicClientApplication, InteractionRequiredAuthError };
});

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

const A = { tenantId: 'aaaaaaaa-0000-0000-0000-000000000001', name: 'Acme', roles: ['editor'] };
const B = { tenantId: 'bbbbbbbb-0000-0000-0000-000000000002', name: 'Globex', roles: ['viewer'] };

interface Call { url: string; headers: Headers }

/** A fake server behind global fetch: config, me/tenants, tasks, plus per-path overrides. */
function fakeServer(config: Record<string, unknown>, tenants: unknown) {
  const calls: Call[] = [];
  const overrides = new Map<string, () => Response>();
  global.fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, headers: new Headers(init?.headers) });
    const override = overrides.get(url);
    if (override) return override();
    if (url.endsWith('/configuration.json')) return json(config);
    if (url.endsWith('/api/me/tenants')) return json(tenants);
    if (url.endsWith('/api/tasks')) return json([]);
    return json({});
  }) as any;
  return {
    calls,
    override: (url: string, make: () => Response) => overrides.set(url, make),
    to: (suffix: string) => calls.filter(c => c.url.endsWith(suffix)),
  };
}

const multi = { api: { root: '/api' }, tenancy: { mode: 'Multi', header: 'X-Tenant-Id' } };
const registered = (...tenants: unknown[]) => ({ registered: true, displayName: 'P', tenants });

const created: Array<{ tasks: { stop(): void } }> = [];
const make = async () => {
  const client = await createPlatformClient();
  created.push(client);
  return client;
};

describe('createPlatformClient with tenancy', () => {
  beforeEach(() => {
    resetConfigCache();
    localStorage.clear();
  });
  afterEach(() => {
    created.splice(0).forEach(c => c.tasks.stop());
    vi.restoreAllMocks();
  });

  it('is null and sends no tenant header or me/tenants request when tenancy is off', async () => {
    const server = fakeServer({ api: { root: '/api' } }, registered(A));

    const { tenant, api } = await make();
    await api.get('projects');

    expect(tenant).toBeNull();
    expect(server.to('/api/me/tenants')).toHaveLength(0);
    expect(server.to('/api/projects')[0].headers.has('X-Tenant-Id')).toBe(false);
  });

  it('treats an explicit None mode like no tenancy', async () => {
    const server = fakeServer({ api: { root: '/api' }, tenancy: { mode: 'None', header: 'X-Tenant-Id' } }, registered(A));

    const { tenant } = await make();

    expect(tenant).toBeNull();
    expect(server.to('/api/me/tenants')).toHaveLength(0);
  });

  it('does not poll tasks until a tenant is selected, then polls with that tenant', async () => {
    const server = fakeServer(multi, registered(A, B));
    const { tenant } = await make();

    await vi.waitFor(() => expect(tenant!.state.status).toBe('selecting'));
    expect(server.to('/api/tasks')).toHaveLength(0);

    tenant!.select(B.tenantId);

    await vi.waitFor(() => expect(server.to('/api/tasks').length).toBeGreaterThan(0));
    expect(server.to('/api/tasks')[0].headers.get('X-Tenant-Id')).toBe(B.tenantId);
  });

  it('starts polling by itself when the only tenant is selected automatically', async () => {
    const server = fakeServer(multi, registered(A));
    const { tenant } = await make();

    await vi.waitFor(() => expect(tenant!.state.status).toBe('selected'));
    await vi.waitFor(() => expect(server.to('/api/tasks').length).toBeGreaterThan(0));
  });

  it('refreshes the task list when the tenant changes', async () => {
    const server = fakeServer(multi, registered(A, B));
    const { tenant } = await make();
    await vi.waitFor(() => expect(tenant!.state.status).toBe('selecting'));
    tenant!.select(A.tenantId);
    await vi.waitFor(() => expect(server.to('/api/tasks')).toHaveLength(1));

    tenant!.select(B.tenantId);

    await vi.waitFor(() => expect(server.to('/api/tasks')).toHaveLength(2));
    expect(server.to('/api/tasks')[1].headers.get('X-Tenant-Id')).toBe(B.tenantId);
  });

  it('goes back to the picker when the server says a tenant is required, and stops polling', async () => {
    const server = fakeServer(multi, registered(A, B));
    const { tenant, api } = await make();
    await vi.waitFor(() => expect(tenant!.state.status).toBe('selecting'));
    tenant!.select(A.tenantId);
    await vi.waitFor(() => expect(tenant!.state.status).toBe('selected'));
    server.override('/api/projects', () => json({ error: 'tenant_required' }, 409));

    await api.get('projects').catch(() => {});

    expect(tenant!.state.status).toBe('selecting');
    expect(tenant!.headers()).toEqual({});
    expect(localStorage.getItem([...Object.keys(localStorage)].find(k => k.startsWith('ps-tenant:'))!)).toBeNull();
  });

  it('goes back to the picker when a tenant is forbidden', async () => {
    const server = fakeServer(multi, registered(A, B));
    const { tenant, api } = await make();
    await vi.waitFor(() => expect(tenant!.state.status).toBe('selecting'));
    tenant!.select(B.tenantId);
    server.override('/api/projects', () => json({ error: 'tenant_forbidden' }, 403));

    await api.get('projects').catch(() => {});

    expect(tenant!.state.status).toBe('selecting');
  });

  it('reloads the session when the server says the user is not registered', async () => {
    const server = fakeServer(multi, registered(A, B));
    const { tenant, api } = await make();
    await vi.waitFor(() => expect(tenant!.state.status).toBe('selecting'));
    server.override('/api/me/tenants', () => json({ registered: false, displayName: null, tenants: [] }));
    server.override('/api/projects', () => json({ error: 'not_registered' }, 403));

    await api.get('projects').catch(() => {});

    await vi.waitFor(() => expect(tenant!.state.status).toBe('not-registered'));
  });
});

describe('createPlatformClient storage keys', () => {
  const withAuth = {
    ...multi,
    auth: { tenantId: 'common', clientId: 'client', scopes: ['api://x/access_as_user'] },
  };

  beforeEach(() => {
    resetConfigCache();
    localStorage.clear();
  });
  afterEach(() => {
    created.splice(0).forEach(c => c.tasks.stop());
    vi.restoreAllMocks();
  });

  async function signInAs(homeAccountId: string) {
    idp.homeAccountId = homeAccountId;
    resetConfigCache();
    const client = await make();
    await vi.waitFor(() => expect(['selecting', 'selected']).toContain(client.tenant!.state.status));
    return client;
  }

  it('remembers the choice per account, so another account never reuses it', async () => {
    fakeServer(withAuth, registered(A, B));

    const first = await signInAs('acct-1');
    first.tenant!.select(B.tenantId);
    const second = await signInAs('acct-2');

    expect(second.tenant!.state.status).toBe('selecting');
    const keys = Object.keys(localStorage).filter(k => k.startsWith('ps-tenant:')).sort();
    expect(keys).toHaveLength(1);
    expect(keys[0]).toContain('acct-1');

    second.tenant!.select(A.tenantId);
    expect(Object.keys(localStorage).filter(k => k.startsWith('ps-tenant:'))).toHaveLength(2);

    const firstAgain = await signInAs('acct-1');
    expect(firstAgain.tenant!.state).toMatchObject({ status: 'selected', tenant: { tenantId: B.tenantId } });
  });

  it('sends the access token and the tenant on the same request', async () => {
    const server = fakeServer(withAuth, registered(A));
    const { tenant, api } = await signInAs('acct-1');
    await vi.waitFor(() => expect(tenant!.state.status).toBe('selected'));

    await api.get('projects');

    const headers = server.to('/api/projects')[0].headers;
    expect(headers.get('Authorization')).toBe('Bearer token');
    expect(headers.get('X-Tenant-Id')).toBe(A.tenantId);
  });
});
