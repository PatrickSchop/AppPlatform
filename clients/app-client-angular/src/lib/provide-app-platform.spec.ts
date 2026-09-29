import { TestBed } from '@angular/core/testing';
import { provideAppPlatform } from './provide-app-platform';
import { APP_CONFIG, API_CLIENT, AUTH_CLIENT } from './injection-tokens';
import { vi } from 'vitest';

describe('provideAppPlatform', () => {
  const mockFetch = vi.fn(async () => ({
    ok: true,
    status: 200,
    json: async () => ({
      api: { root: 'http://api.test' },
      auth: { tenantId: 'test', clientId: 'test', scopes: [] },
    }),
  }));

  beforeEach(() => {
    global.fetch = mockFetch as any;
  });

  it('resolves APP_CONFIG after initialisation', async () => {
    TestBed.configureTestingModule({
      providers: [provideAppPlatform()],
    });

    const config = TestBed.inject(APP_CONFIG);
    expect(config).toBeDefined();
  });

  it('provides API_CLIENT token', async () => {
    TestBed.configureTestingModule({
      providers: [provideAppPlatform()],
    });

    const apiClient = TestBed.inject(API_CLIENT);
    expect(apiClient).toBeDefined();
  });

  it('provides AUTH_CLIENT token', async () => {
    TestBed.configureTestingModule({
      providers: [provideAppPlatform()],
    });

    const authClient = TestBed.inject(AUTH_CLIENT);
    expect(authClient).toBeDefined();
  });
});
