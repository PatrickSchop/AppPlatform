import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { resetConfigCache, TenantSession } from '@PS/app-client';
import { provideAppPlatform, resetPlatformClientForTests } from './provide-app-platform';
import { API_CLIENT, APP_CONFIG, AUTH_CLIENT, TENANT_SESSION } from './injection-tokens';
import { registered, TENANT_A } from './testing';

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });

function serve(config: Record<string, unknown>) {
  global.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.endsWith('/configuration.json')) return json(config);
    if (url.endsWith('/me/tenants')) return json(registered(TENANT_A));
    return json([]);
  }) as any;
}

async function init() {
  TestBed.configureTestingModule({ providers: [provideAppPlatform()] });
  await TestBed.inject(ApplicationInitStatus).donePromise;
}

describe('provideAppPlatform', () => {
  beforeEach(() => {
    resetPlatformClientForTests();
    resetConfigCache();
  });

  it('provides the loaded configuration, not a promise of it', async () => {
    serve({ api: { root: '/api' } });
    await init();

    expect(TestBed.inject(APP_CONFIG)).toEqual({ api: { root: '/api' } });
  });

  it('provides a usable ApiClient', async () => {
    serve({ api: { root: '/api' } });
    await init();

    const api = TestBed.inject(API_CLIENT);
    expect(typeof api.get).toBe('function');
    expect(api).not.toBeInstanceOf(Promise);
  });

  it('provides no auth client and no tenant session when neither is configured', async () => {
    serve({ api: { root: '/api' } });
    await init();

    expect(TestBed.inject(AUTH_CLIENT)).toBeNull();
    expect(TestBed.inject(TENANT_SESSION)).toBeNull();
  });

  it('provides the tenant session when the app runs in a tenancy mode', async () => {
    serve({ api: { root: '/api' }, tenancy: { mode: 'Multi', header: 'X-Tenant-Id' } });
    await init();

    expect(TestBed.inject(TENANT_SESSION)).toBeInstanceOf(TenantSession);
  });
});
