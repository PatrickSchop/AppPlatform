import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { adminGuard } from './admin.guard';
import { API_CLIENT, AUTH_CLIENT } from '@PS/app-client-angular';
import type { MyTenantsResponse } from '../services/models';

describe('adminGuard', () => {
  it('should allow admin user (registered with admin role)', async () => {
    const mockAuth = {
      getAccount: () => ({ oid: 'user-123' })
    };

    const mockApi = {
      get: vi.fn().mockResolvedValue({
        registered: true,
        tenants: [{ roles: ['admin'], name: 'Test App' }]
      } as MyTenantsResponse)
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AUTH_CLIENT, useValue: mockAuth },
        { provide: API_CLIENT, useValue: mockApi }
      ]
    });

    const result = await TestBed.runInInjectionContext(async () => {
      return await adminGuard({} as any, {} as any);
    });

    expect(result).toBe(true);
    expect(mockApi.get).toHaveBeenCalledWith('me/tenants');
  });

  it('should redirect non-admin user to /no-access', async () => {
    const mockAuth = {
      getAccount: () => ({ oid: 'user-123' })
    };

    const mockApi = {
      get: vi.fn().mockResolvedValue({
        registered: true,
        tenants: [{ roles: ['member'], name: 'Test App' }]
      } as MyTenantsResponse)
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AUTH_CLIENT, useValue: mockAuth },
        { provide: API_CLIENT, useValue: mockApi }
      ]
    });

    const router = TestBed.inject(Router);
    vi.spyOn(router, 'createUrlTree').mockReturnValue({} as any);

    const result = await TestBed.runInInjectionContext(async () => {
      return await adminGuard({} as any, {} as any);
    });

    expect(router.createUrlTree).toHaveBeenCalledWith(['/no-access']);
  });

  it('should redirect unregistered user to /no-access', async () => {
    const mockAuth = {
      getAccount: () => ({ oid: 'user-123' })
    };

    const mockApi = {
      get: vi.fn().mockResolvedValue({
        registered: false,
        tenants: []
      } as MyTenantsResponse)
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AUTH_CLIENT, useValue: mockAuth },
        { provide: API_CLIENT, useValue: mockApi }
      ]
    });

    const router = TestBed.inject(Router);
    vi.spyOn(router, 'createUrlTree').mockReturnValue({} as any);

    const result = await TestBed.runInInjectionContext(async () => {
      return await adminGuard({} as any, {} as any);
    });

    expect(router.createUrlTree).toHaveBeenCalledWith(['/no-access']);
  });

  it('should handle API errors and redirect to /no-access', async () => {
    const mockAuth = {
      getAccount: () => ({ oid: 'user-123' })
    };

    const mockApi = {
      get: vi.fn().mockRejectedValue(new Error('API Error'))
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: AUTH_CLIENT, useValue: mockAuth },
        { provide: API_CLIENT, useValue: mockApi }
      ]
    });

    const router = TestBed.inject(Router);
    vi.spyOn(router, 'createUrlTree').mockReturnValue({} as any);
    vi.spyOn(console, 'error').mockImplementation(() => {});

    const result = await TestBed.runInInjectionContext(async () => {
      return await adminGuard({} as any, {} as any);
    });

    expect(router.createUrlTree).toHaveBeenCalledWith(['/no-access']);
    expect(console.error).toHaveBeenCalledWith('Admin guard error:', expect.any(Error));
  });
});
