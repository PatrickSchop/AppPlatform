import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { resetConfigCache } from '@PS/app-client';
import { provideAppPlatform, resetPlatformClientForTests } from './provide-app-platform';
import { provideTenancy } from './provide-tenancy';
import { TenantService } from './tenant.service';
import { registered, TENANT_A, TENANT_B } from './testing';

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200 });

/** me/tenants answers slowly, so the test can tell "waited for it" from "did not". */
function serve(config: Record<string, unknown>, tenants = registered(TENANT_A, TENANT_B)) {
  global.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.endsWith('/configuration.json')) return json(config);
    if (url.endsWith('/me/tenants')) {
      await new Promise(resolve => setTimeout(resolve, 30));
      return json(tenants);
    }
    return json([]);
  }) as any;
}

describe('provideTenancy', () => {
  beforeEach(() => {
    resetPlatformClientForTests();
    resetConfigCache();
    localStorage.clear();
  });

  it('has the tenants ready before the first render', async () => {
    serve({ api: { root: '/api' }, tenancy: { mode: 'Multi', header: 'X-Tenant-Id' } });
    TestBed.configureTestingModule({ providers: [provideAppPlatform(), provideTenancy()] });

    await TestBed.inject(ApplicationInitStatus).donePromise;

    expect(TestBed.inject(TenantService).state().status).toBe('selecting');
  });

  it('works whichever way round the two providers are listed', async () => {
    serve({ api: { root: '/api' }, tenancy: { mode: 'Multi', header: 'X-Tenant-Id' } });
    TestBed.configureTestingModule({ providers: [provideTenancy(), provideAppPlatform()] });

    await TestBed.inject(ApplicationInitStatus).donePromise;

    expect(TestBed.inject(TenantService).state().status).toBe('selecting');
  });

  it('does nothing when the app does not run in a tenancy mode', async () => {
    serve({ api: { root: '/api' } });
    TestBed.configureTestingModule({ providers: [provideAppPlatform(), provideTenancy()] });

    await TestBed.inject(ApplicationInitStatus).donePromise;

    const tenants = TestBed.inject(TenantService);
    expect(tenants.enabled).toBe(false);
    expect((global.fetch as any).mock.calls.some((c: any[]) => String(c[0]).endsWith('/me/tenants'))).toBe(false);
  });
});
