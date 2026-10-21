import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AcceptInviteComponent } from './accept-invite.component';
import { AdminService } from '../../services/admin.service';
import { ToastService } from '../../shared/toast.service';
import { AUTH_CLIENT, API_CLIENT } from '@PS/app-client-angular';
import type { MyTenantsResponse } from '../../services/models';

describe('AcceptInviteComponent', () => {
  let component: AcceptInviteComponent;
  let fixture: ComponentFixture<AcceptInviteComponent>;
  let adminService: AdminService;
  let toastService: ToastService;
  let router: Router;
  let activatedRoute: ActivatedRoute;
  let mockAuthClient: any;

  beforeEach(async () => {
    mockAuthClient = {
      signIn: vi.fn().mockResolvedValue(undefined)
    };

    await TestBed.configureTestingModule({
      imports: [AcceptInviteComponent],
      providers: [
        AdminService,
        ToastService,
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => key === 'token' ? 'test-token' : null
              }
            }
          }
        },
        { provide: AUTH_CLIENT, useValue: mockAuthClient },
        { provide: API_CLIENT, useValue: { get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AcceptInviteComponent);
    component = fixture.componentInstance;
    adminService = TestBed.inject(AdminService);
    toastService = TestBed.inject(ToastService);
    router = TestBed.inject(Router);
    activatedRoute = TestBed.inject(ActivatedRoute);
    vi.spyOn(toastService, 'show');
    vi.spyOn(router, 'navigate');
  });

  it('should map 410 status to expiration message', async () => {
    const error = { status: 410, error: {} };
    vi.spyOn(adminService, 'getMyTenants').mockResolvedValue({
      registered: true,
      tenants: []
    } as MyTenantsResponse);

    vi.spyOn(adminService, 'previewInvitation').mockRejectedValue(error);

    await component.ngOnInit();
    fixture.detectChanges();

    expect(component.error()).toContain('expired');
  });

  it('should map 409 identity_already_registered error', async () => {
    const error = {
      status: 409,
      error: { code: 'identity_already_registered' }
    };
    vi.spyOn(adminService, 'getMyTenants').mockResolvedValue({
      registered: true,
      tenants: []
    } as MyTenantsResponse);

    vi.spyOn(adminService, 'previewInvitation').mockRejectedValue(error);

    await component.ngOnInit();
    fixture.detectChanges();

    expect(component.error()).toContain('already registered');
  });

  it('should map 409 already_bound error', async () => {
    const error = {
      status: 409,
      error: { code: 'already_bound' }
    };
    vi.spyOn(adminService, 'getMyTenants').mockResolvedValue({
      registered: true,
      tenants: []
    } as MyTenantsResponse);

    vi.spyOn(adminService, 'previewInvitation').mockRejectedValue(error);

    await component.ngOnInit();
    fixture.detectChanges();

    expect(component.error()).toContain('already linked');
  });

  it('should show application names on success', async () => {
    const tenantResponse: MyTenantsResponse = {
      registered: true,
      tenants: [
        { id: 'tenant-1', name: 'App 1', roles: ['member'] },
        { id: 'tenant-2', name: 'App 2', roles: ['member'] }
      ]
    };

    vi.spyOn(adminService, 'getMyTenants').mockResolvedValue(tenantResponse);
    vi.spyOn(adminService, 'previewInvitation').mockResolvedValue({
      displayName: 'Test User',
      email: 'test@example.com'
    });
    vi.spyOn(adminService, 'acceptInvitation').mockResolvedValue({ success: true });

    await component.ngOnInit();
    fixture.detectChanges();

    await component.acceptInvitation();
    fixture.detectChanges();

    const apps = component.applications();
    expect(apps.length).toBe(2);
    expect(apps).toContain('App 1');
    expect(apps).toContain('App 2');
  });

  it('should handle missing token', async () => {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [AcceptInviteComponent],
      providers: [
        AdminService,
        ToastService,
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: () => null
              }
            }
          }
        },
        { provide: AUTH_CLIENT, useValue: mockAuthClient },
        { provide: API_CLIENT, useValue: { get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AcceptInviteComponent);
    component = fixture.componentInstance;

    fixture.detectChanges();
    await fixture.whenStable();

    expect(component.error()).toContain('Invalid');
  });
});
