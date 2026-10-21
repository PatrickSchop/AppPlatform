import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { UserListComponent } from './user-list.component';
import { AdminService } from '../../services/admin.service';
import { API_CLIENT } from '@PS/app-client-angular';
import type { ApplicationDetail, ApplicationSummary, RoleSummary, TeamSummary } from '../../services/models';

describe('UserListComponent: creating a user', () => {
  let fixture: ComponentFixture<UserListComponent>;
  let component: UserListComponent;
  let adminService: AdminService;

  const app: ApplicationSummary = {
    id: 'app-1', key: 'management', displayName: 'Application management',
    tenancy: 'Single', tenantCount: 1, userCount: 1, isDisabled: false
  };
  const role = (id: string, name: string, isDeprecated = false): RoleSummary =>
    ({ id, name, displayName: name, isDeprecated, assignmentCount: 0 });
  const detail: ApplicationDetail = {
    id: 'app-1', key: 'management', displayName: 'Application management', tenancy: 'Single', isDisabled: false,
    roles: [role('role-admin', 'admin'), role('role-old', 'old', true)],
    tenants: [{ id: 'tenant-1', name: 'Default', isDisabled: false }]
  };
  const defaultTeam: TeamSummary = { id: 'team-default', name: 'Default', isDefault: true };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserListComponent],
      providers: [provideRouter([]), { provide: API_CLIENT, useValue: { get: vi.fn() } }]
    }).compileComponents();

    fixture = TestBed.createComponent(UserListComponent);
    component = fixture.componentInstance;
    adminService = TestBed.inject(AdminService);
    vi.spyOn(adminService, 'getApplications').mockResolvedValue([app]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue(detail);
    vi.spyOn(adminService, 'getTeams').mockResolvedValue([defaultTeam]);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    await component.ngOnInit();
    fixture.detectChanges();
  });

  it('puts the new user in the default team and offers only active roles', () => {
    expect(component.selectedTeamId()).toBe('team-default');
    expect(component.roles().map(r => r.name)).toEqual(['admin']);
    // One tenant and one team: nothing to choose, so the form does not ask.
    expect(fixture.nativeElement.textContent).not.toContain('Tenant');
  });

  it('creates the user in one call and shows the invitation link straight away', async () => {
    const createUser = vi.spyOn(adminService, 'createUser').mockResolvedValue({
      userId: 'user-q', displayName: 'Q',
      invitation: { invitationId: 'inv-1', url: 'https://example.test/invite/abc', expiresUtc: '2026-11-01T00:00:00Z' }
    });
    component.newUserDisplayName.set('Q');
    component.newUserEmail.set('q@example.test');
    component.selectedRoleIds.set(['role-admin']);

    await component.createUser();
    fixture.detectChanges();

    expect(createUser).toHaveBeenCalledWith('app-1', {
      displayName: 'Q', email: 'q@example.test', teamId: 'team-default', roleIds: ['role-admin']
    });
    expect(component.showInviteDialog()).toBe(true);
    expect(component.inviteUrl()).toBe('https://example.test/invite/abc');
    expect(fixture.nativeElement.querySelector('app-invite-dialog')).not.toBeNull();
    expect(component.newUserDisplayName()).toBe('');
  });

  it('creates a user without a role when none is ticked', async () => {
    const createUser = vi.spyOn(adminService, 'createUser').mockResolvedValue({
      userId: 'user-q', displayName: 'Q',
      invitation: { invitationId: 'inv-1', url: 'u', expiresUtc: '2026-11-01T00:00:00Z' }
    });
    component.newUserDisplayName.set('Q');

    await component.createUser();

    expect(createUser.mock.calls[0][1].roleIds).toEqual([]);
  });

  it('does not show an invitation when creating fails', async () => {
    vi.spyOn(adminService, 'createUser').mockRejectedValue({ status: 400, body: { error: 'invalid_roles' } });
    component.newUserDisplayName.set('Q');

    await component.createUser();

    expect(component.showInviteDialog()).toBe(false);
    expect(component.creating()).toBe(false);
  });

  it('lets the admin pick tenant and team when the application has several', async () => {
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      ...detail,
      tenancy: 'Multi',
      tenants: [{ id: 't1', name: 'Acme', isDisabled: false }, { id: 't2', name: 'Globex', isDisabled: false }]
    });
    const getTeams = vi.spyOn(adminService, 'getTeams').mockResolvedValue([
      { id: 'x', name: 'Ops', isDefault: false }, defaultTeam
    ]);

    await component.onAppChange();
    fixture.detectChanges();

    expect(getTeams).toHaveBeenCalledWith('app-1', 't1');
    expect(component.selectedTeamId()).toBe('team-default');
    expect(fixture.nativeElement.textContent).toContain('Tenant');
    expect(fixture.nativeElement.textContent).toContain('Team');
  });
});
