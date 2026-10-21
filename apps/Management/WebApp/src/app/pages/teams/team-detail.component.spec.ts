import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { TeamDetailComponent } from './team-detail.component';
import { AdminService } from '../../services/admin.service';
import { ToastService } from '../../shared/toast.service';
import { ActivatedRoute } from '@angular/router';
import { API_CLIENT } from '@PS/app-client-angular';
import type { MemberSummary, RoleSummary } from '../../services/models';

describe('TeamDetailComponent', () => {
  let component: TeamDetailComponent;
  let fixture: ComponentFixture<TeamDetailComponent>;
  let adminService: AdminService;
  let toastService: ToastService;

  const mockMembers: MemberSummary[] = [
    {
      id: 'member-1',
      displayName: 'User One',
      roles: [{ id: 'role-1', name: 'Admin' }]
    },
    {
      id: 'member-2',
      displayName: 'User Two',
      roles: [{ id: 'role-2', name: 'Viewer' }]
    }
  ];

  const mockRoles: RoleSummary[] = [
    { id: 'role-1', name: 'Admin' },
    { id: 'role-2', name: 'Viewer' },
    { id: 'role-3', name: 'Editor' }
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
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      name: 'Test App',
      roles: mockRoles
    } as any);

    await component.ngOnInit();
    fixture.detectChanges();

    const checkboxes = fixture.nativeElement.querySelectorAll('table input[type="checkbox"]');
    // 2 members * 3 roles = 6 checkboxes
    expect(checkboxes.length).toBe(6);
  });

  it('should call setMemberRoles when checkbox is toggled', async () => {
    vi.spyOn(adminService, 'getMembers').mockResolvedValue(mockMembers);
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      name: 'Test App',
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
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      name: 'Test App',
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
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      name: 'Test App',
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
    vi.spyOn(adminService, 'getApplication').mockResolvedValue({
      id: 'app-1',
      name: 'Test App',
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
});
