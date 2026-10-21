import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AcceptInviteComponent } from './accept-invite.component';
import { AdminService } from '../../services/admin.service';
import { ToastService } from '../../shared/toast.service';
import { AUTH_CLIENT, API_CLIENT } from '@PS/app-client-angular';

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
      getAccount: () => null,
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

  const signedIn = () => { mockAuthClient.getAccount = () => ({ username: 'q@example.com', name: 'Q' }); };

  it('should map 410 status to expiration message', async () => {
    signedIn();
    vi.spyOn(adminService, 'previewInvitation').mockRejectedValue({ status: 410, body: { } });

    await component.ngOnInit();
    fixture.detectChanges();

    expect(component.error()).toContain('expired');
  });

  it('should map 409 identity_already_registered error', async () => {
    signedIn();
    vi.spyOn(adminService, 'previewInvitation').mockRejectedValue({
      status: 409,
      body: { error: 'identity_already_registered' }
    });

    await component.ngOnInit();
    fixture.detectChanges();

    expect(component.error()).toContain('already registered');
  });

  it('should map 409 already_bound error', async () => {
    signedIn();
    vi.spyOn(adminService, 'previewInvitation').mockRejectedValue({
      status: 409,
      body: { error: 'already_bound' }
    });

    await component.ngOnInit();
    fixture.detectChanges();

    expect(component.error()).toContain('already linked');
  });

  it('should sign in first when there is no identity-provider session', async () => {
    mockAuthClient.getAccount = () => null;
    const preview = vi.spyOn(adminService, 'previewInvitation');

    await component.ngOnInit();

    expect(mockAuthClient.signIn).toHaveBeenCalled();
    expect(preview).not.toHaveBeenCalled();
  });

  it('should preview the invitation for a signed-in user who is not registered yet', async () => {
    // An invitee is unregistered by definition; being unregistered must not send them back to sign-in.
    signedIn();
    const registeredCheck = vi.spyOn(adminService, 'getMyTenants');
    vi.spyOn(adminService, 'previewInvitation').mockResolvedValue({
      displayName: 'Q',
      applications: ['management'],
      expiresUtc: '2026-11-01T00:00:00Z'
    });

    await component.ngOnInit();

    expect(mockAuthClient.signIn).not.toHaveBeenCalled();
    expect(registeredCheck).not.toHaveBeenCalled();
    expect(component.previewApps()).toEqual(['management']);
  });

  it('should show the granted applications after accepting', async () => {
    signedIn();
    vi.spyOn(adminService, 'previewInvitation').mockResolvedValue({
      displayName: 'Q',
      applications: ['management', 'recipes'],
      expiresUtc: '2026-11-01T00:00:00Z'
    });
    vi.spyOn(adminService, 'acceptInvitation').mockResolvedValue({ displayName: 'Q' });

    await component.ngOnInit();
    await component.acceptInvitation();

    expect(component.accepted()).toBe(true);
    expect(component.applications()).toEqual(['management', 'recipes']);
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
