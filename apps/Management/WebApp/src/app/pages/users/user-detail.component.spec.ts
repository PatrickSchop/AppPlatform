import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { UserDetailComponent } from './user-detail.component';
import { AdminService } from '../../services/admin.service';
import { API_CLIENT } from '@patrickschop/app-client-angular';
import type { UserDetail } from '../../services/models';

describe('UserDetailComponent: invitations tab', () => {
  let fixture: ComponentFixture<UserDetailComponent>;
  let component: UserDetailComponent;
  let adminService: AdminService;

  const user = (status: string): UserDetail => ({
    id: 'user-1', displayName: 'Q', email: null, status, createdUtc: '2026-10-21T00:00:00Z', memberships: []
  });

  async function open(status: string) {
    await TestBed.configureTestingModule({
      imports: [UserDetailComponent],
      providers: [
        provideRouter([]),
        { provide: API_CLIENT, useValue: { get: vi.fn() } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => 'user-1' },
              queryParamMap: { get: () => 'app-1' }
            }
          }
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(UserDetailComponent);
    component = fixture.componentInstance;
    adminService = TestBed.inject(AdminService);
    vi.spyOn(adminService, 'getUser').mockResolvedValue(user(status));
    vi.spyOn(adminService, 'getInvitations').mockResolvedValue([]);
    await component.ngOnInit();
    component.activeTab.set('invitations');
    fixture.detectChanges();
  }

  const button = () =>
    Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'))
      .find(b => b.textContent!.includes('invite link'));

  it('offers a new invite link only while the invitation is still outstanding', async () => {
    await open('invited');

    expect(button()?.textContent).toContain('Create new invite link');
    expect(fixture.nativeElement.textContent).toContain('lost or has expired');
  });

  it('does not offer a new link once the user has accepted', async () => {
    await open('active');

    expect(button()).toBeUndefined();
    expect(fixture.nativeElement.textContent).toContain('accepted their invitation');
  });

  it('shows the link in the dialog when a new one is created', async () => {
    await open('invited');
    vi.spyOn(adminService, 'createInvitation').mockResolvedValue({
      invitationId: 'inv-2', url: 'https://example.test/invite/new', expiresUtc: '2026-11-01T00:00:00Z'
    });

    await component.createInvite();
    fixture.detectChanges();

    expect(component.inviteUrl()).toBe('https://example.test/invite/new');
    expect(fixture.nativeElement.querySelector('app-invite-dialog')).not.toBeNull();
  });
});
