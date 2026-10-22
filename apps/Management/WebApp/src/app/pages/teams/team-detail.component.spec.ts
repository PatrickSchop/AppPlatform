import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TeamDetailComponent } from './team-detail.component';
import { AdminService } from '../../services/admin.service';
import { ToastService } from '../../shared/toast.service';
import { ActivatedRoute } from '@angular/router';
import { API_CLIENT } from '@patrickschop/app-client-angular';
import type { MemberSummary, RoleSummary } from '../../services/models';

describe('TeamDetailComponent', () => {
  let component: TeamDetailComponent;
  let fixture: ComponentFixture<TeamDetailComponent>;
  let adminService: AdminService;
  let toastService: ToastService;

  const mockMembers: MemberSummary[] = [
    { memberId: 'member-1', userId: 'user-1', displayName: 'User One', roles: ['Admin'] },
    { memberId: 'member-2', userId: 'user-2', displayName: 'User Two', roles: ['Viewer'] }
  ];

  const mockRoles: RoleSummary[] = [
    { id: 'role-1', name: 'Admin', displayName: 'Admin', isDeprecated: false, assignmentCount: 1 },
    { id: 'role-2', name: 'Viewer', displayName: 'Viewer', isDeprecated: false, assignmentCount: 1 },
    { id: 'role-3', name: 'Editor', displayName: 'Editor', isDeprecated: false, assignmentCount: 0 },
    { id: 'role-4', name: 'Old', displayName: 'Old', isDeprecated: true, assignmentCount: 0 }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TeamDetailComponent],
      providers: [
        AdminService,
        ToastService,
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => {
                  const params: Record<string, string> = {
                    appId: 'app-1',
                    tenantId: 'tenant-1',
                    teamId: 'team-1'
                  };
                  return params[key] || null;
                }
              }
            }
          }
        },
        { provide: API_CLIENT, useValue: { get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(TeamDetailComponent);
    component = fixture.componentInstance;
    adminService = TestBed.inject(AdminService);
    toastService = TestBed.inject(ToastService);

    vi.spyOn(toastService, 'show');
  });

  it('should render a checkbox for each available role per member', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      displayName: 'Test App',
      roles: mockRoles
    } as any);

    await component.ngOnInit();
    fixture.detectChanges();

    const checkboxes = fixture.nativeElement.querySelectorAll('table input[type="checkbox"]');
    // 2 members * 3 active roles = 6 checkboxes (the deprecated role gets no column)
    expect(checkboxes.length).toBe(6);
  });

  it('should call setMemberRoles when checkbox is toggled', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      displayName: 'Test App',
      roles: mockRoles
    } as any);
    vi.spyOn(adminService, 'setMemberRoles').mockResolvedValue({});

    await component.ngOnInit();
    fixture.detectChanges();

    const checkboxes = fixture.nativeElement.querySelectorAll('table input[type="checkbox"]');
    // Click the checkbox for member-1, role-2 (currently unchecked)
    checkboxes[1].click();

    await fixture.whenStable();

    // Should call setMemberRoles with the updated roles
    expect(adminService.setMemberRoles).toHaveBeenCalledWith(
      'app-1',
      'tenant-1',
      'team-1',
      'member-1',
      expect.arrayContaining(['role-1', 'role-2'])
    );
  });

  it('should send correct roleIds array when checkbox is checked', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      displayName: 'Test App',
      roles: mockRoles
    } as any);
    vi.spyOn(adminService, 'setMemberRoles').mockResolvedValue({});

    await component.ngOnInit();
    fixture.detectChanges();

    // Member 1 currently has role-1, let's add role-2
    await component.onMemberRoleChange('member-1', 'role-2', {
      target: { checked: true } as any
    } as Event);

    fixture.detectChanges();

    expect(adminService.setMemberRoles).toHaveBeenCalledWith(
      'app-1',
      'tenant-1',
      'team-1',
      'member-1',
      expect.arrayContaining(['role-1', 'role-2'])
    );
  });

  it('should show cache banner after role change', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      displayName: 'Test App',
      roles: mockRoles
    } as any);
    vi.spyOn(adminService, 'setMemberRoles').mockResolvedValue({});

    await component.ngOnInit();
    fixture.detectChanges();

    await component.onMemberRoleChange('member-1', 'role-2', {
      target: { checked: true } as any
    } as Event);

    fixture.detectChanges();

    expect(toastService.show).toHaveBeenCalledWith(
      'Changes can take up to 5 minutes to reach running apps',
      'info'
    );
  });

  it('should handle member not found gracefully', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      displayName: 'Test App',
      roles: mockRoles
    } as any);
    vi.spyOn(adminService, 'setMemberRoles').mockResolvedValue({});

    await component.ngOnInit();
    fixture.detectChanges();

    component.onMemberRoleChange('non-existent-member', 'role-1', {
      target: { checked: true } as any
    } as Event);

    // Should return early without calling the service
    expect(adminService.setMemberRoles).not.toHaveBeenCalled();
  });
  it('points to the Users page when there is nobody left to add', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({ id: 'app-1', displayName: 'Test App', roles: mockRoles } as any);

    await component.ngOnInit();
    fixture.detectChanges();

    const hint = fixture.nativeElement.querySelector('.hint');
    expect(hint.textContent).toContain('create a user');
    expect(hint.querySelector('a').getAttribute('href')).toBe('/users');
  });

  it('lists users who are not in the team yet', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([
      { id: 'user-1', displayName: 'User One', status: 'active', createdUtc: '' },
      { id: 'user-9', displayName: 'Q', status: 'invited', createdUtc: '' }
    ]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({ id: 'app-1', displayName: 'Test App', roles: mockRoles } as any);

    await component.ngOnInit();
    fixture.detectChanges();

    const options = Array.from<HTMLOptionElement>(fixture.nativeElement.querySelectorAll('select option')).map(o => o.textContent!.trim());
    expect(options).toEqual(['Select a user…', 'Q']);
    expect(fixture.nativeElement.querySelector('.hint')).toBeNull();
  });
  it('puts the checkbox back and reloads when the server refuses a role change', async () => {
    const getMembers = vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({ id: 'app-1', displayName: 'Test App', roles: mockRoles } as any);
    vi.spyOn(adminService, 'setMemberRoles').mockRejectedValue({ status: 409, body: { error: 'last_admin' } });

    await component.ngOnInit();
    fixture.detectChanges();
    getMembers.mockClear();

    // member-1 holds Admin (first checkbox). Untick it: the browser flips the box before the call.
    const box: HTMLInputElement = fixture.nativeElement.querySelector('table input[type="checkbox"]');
    expect(box.checked).toBe(true);
    box.click();
    expect(box.checked).toBe(false);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(box.checked).toBe(true);
    expect(toastService.show).toHaveBeenCalledWith('This would leave no administrator', 'error');
    expect(getMembers).toHaveBeenCalled();
  });

  it('disables the member checkboxes while a change is being saved', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getUsers').mockResolvedValue([]);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({ id: 'app-1', displayName: 'Test App', roles: mockRoles } as any);
    let finish!: () => void;
    vi.spyOn(adminService, 'setMemberRoles').mockReturnValue(new Promise<void>(r => (finish = r)));

    await component.ngOnInit();
    fixture.detectChanges();
    const first: HTMLInputElement = fixture.nativeElement.querySelector('table input[type="checkbox"]');
    const pending = component.onMemberRoleChange('member-1', 'role-2', { target: { checked: true } } as any);
    fixture.detectChanges();

    expect(first.disabled).toBe(true);
    finish();
    await pending;
    fixture.detectChanges();
    expect(first.disabled).toBe(false);
  });
});
