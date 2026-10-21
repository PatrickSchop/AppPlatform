import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { AdminService } from '../../services/admin.service';
import { MemberSummary, RoleSummary, UserSummary } from '../../services/models';
import { ToastService } from '../../shared/toast.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../shared/breadcrumb.component';
import { getErrorMessage } from '../../shared/error-messages';

@Component({
  selector: 'app-team-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, BreadcrumbComponent],
  template: `
    <app-breadcrumb [items]="breadcrumbs()" />
    <h1>Team Members</h1>

    <div class="add-member">
      <h3>Add Member</h3>
      <div class="form-group">
        <label>User</label>
        <select [(ngModel)]="newMemberId">
          <option value="">Select a user…</option>
          @for (user of availableUsers(); track user.id) {
            <option [value]="user.id">{{ user.displayName }}</option>
          }
        </select>
        @if (availableUsers().length === 0) {
          <p class="hint">Everyone who has a place in this application is already in this team. New people are added from <a routerLink="/users">Users</a> (create a user), which also gives you their invitation link.</p>
        }
      </div>
      <div class="roles-checkboxes">
        @for (role of roles(); track role.id) {
          <label>
            <input type="checkbox" [checked]="newMemberRoles.includes(role.id)"
                   (change)="toggleRoleSelection(role.id, $event)" />
            {{ role.name }}
          </label>
        }
      </div>
      <button (click)="onAddMember()" [disabled]="!newMemberId" class="btn-primary">Add Member</button>
    </div>

    <div class="members-table" style="margin-top: 40px;">
      <h3>Members</h3>
      @if (members().length === 0) {
        <p>No members found.</p>
      } @else {
        <table>
          <thead>
            <tr>
              <th>Display Name</th>
              @for (role of roles(); track role.id) {
                <th>{{ role.name }}</th>
              }
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            @for (member of members(); track member.memberId) {
              <tr>
                <td>{{ member.displayName }}</td>
                @for (role of roles(); track role.id) {
                  <td>
                    <input type="checkbox"
                           [checked]="member.roles.includes(role.name)"
                           (change)="onMemberRoleChange(member.memberId, role.id, $event)" />
                  </td>
                }
                <td>
                  <button (click)="onRemoveMember(member.memberId)" class="btn-danger btn-sm">Remove</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`
    .hint {
      margin: 8px 0 0;
      font-size: 14px;
      color: #6b7280;
    }
    .add-member {
      background: white;
      padding: 16px;
      border-radius: 8px;
      border: 1px solid #e5e7eb;
    }
    h3 {
      margin-top: 0;
    }
    .form-group {
      margin-bottom: 16px;
    }
    label {
      display: block;
      margin-bottom: 8px;
      font-weight: 500;
    }
    input[type="text"] {
      width: 100%;
      max-width: 300px;
      padding: 8px;
      border: 1px solid #d1d5db;
      border-radius: 4px;
    }
    .roles-checkboxes {
      margin-bottom: 16px;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .roles-checkboxes label {
      display: flex;
      align-items: center;
      margin-bottom: 0;
    }
    .roles-checkboxes input {
      margin-right: 8px;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      background: white;
      margin-top: 16px;
    }
    th, td {
      padding: 12px;
      text-align: left;
      border-bottom: 1px solid #e5e7eb;
    }
    .btn-primary, .btn-danger {
      padding: 8px 16px;
      border: none;
      border-radius: 4px;
      cursor: pointer;
    }
    .btn-primary {
      background-color: #3b82f6;
      color: white;
    }
    .btn-primary:disabled {
      background-color: #d1d5db;
      cursor: not-allowed;
    }
    .btn-danger {
      background-color: #ef4444;
      color: white;
    }
    .btn-sm {
      padding: 4px 8px;
      font-size: 12px;
    }
  `]
})
export class TeamDetailComponent implements OnInit {
  private adminService = inject(AdminService);
  private toastService = inject(ToastService);
  private route = inject(ActivatedRoute);

  appId = '';
  tenantId = '';
  teamId = '';

  members = signal<MemberSummary[]>([]);
  roles = signal<RoleSummary[]>([]);
  users = signal<UserSummary[]>([]);
  availableUsers = computed(() => {
    const memberIds = new Set(this.members().map(m => m.userId));
    return this.users().filter(u => !memberIds.has(u.id) && u.status !== 'disabled');
  });
  breadcrumbs = signal<BreadcrumbItem[]>([]);

  newMemberId = '';
  newMemberRoles: string[] = [];

  async ngOnInit(): Promise<void> {
    this.appId = this.route.snapshot.paramMap.get('appId') || '';
    this.tenantId = this.route.snapshot.paramMap.get('tenantId') || '';
    this.teamId = this.route.snapshot.paramMap.get('teamId') || '';
    this.breadcrumbs.set([
      { label: 'Applications', routerLink: ['/applications'] },
      { label: 'Tenant', routerLink: ['/applications', this.appId, 'tenants', this.tenantId] },
      { label: 'Team' }
    ]);
    await this.loadData();
  }

  private async loadData(): Promise<void> {
    try {
      const [members, app, users] = await Promise.all([
        this.adminService.getMembers(this.appId, this.tenantId, this.teamId),
        this.adminService.getApplication(this.appId),
        this.adminService.getUsers(this.appId)
      ]);
      this.members.set(members);
      this.roles.set(app.roles.filter(r => !r.isDeprecated));
      this.users.set(users);
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  toggleRoleSelection(roleId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) {
      this.newMemberRoles.push(roleId);
    } else {
      this.newMemberRoles = this.newMemberRoles.filter(r => r !== roleId);
    }
  }

  async onAddMember(): Promise<void> {
    if (!this.newMemberId) return;
    try {
      await this.adminService.addMember(
        this.appId,
        this.tenantId,
        this.teamId,
        this.newMemberId,
        this.newMemberRoles
      );
      this.newMemberId = '';
      this.newMemberRoles = [];
      this.toastService.show('Member added', 'info');
      await this.loadData();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async onMemberRoleChange(memberId: string, roleId: string, event: Event): Promise<void> {
    const checked = (event.target as HTMLInputElement).checked;
    const member = this.members().find(m => m.memberId === memberId);
    if (!member) return;

    // The API reports a member's roles by name and takes role ids when saving.
    const newRoles = this.roles().filter(r => member.roles.includes(r.name)).map(r => r.id);
    if (checked) {
      newRoles.push(roleId);
    } else {
      newRoles.splice(newRoles.indexOf(roleId), 1);
    }

    try {
      await this.adminService.setMemberRoles(
        this.appId,
        this.tenantId,
        this.teamId,
        memberId,
        newRoles
      );
      this.toastService.show('Changes can take up to 5 minutes to reach running apps', 'info');
      await this.loadData();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async onRemoveMember(memberId: string): Promise<void> {
    if (confirm('Remove this member?')) {
      try {
        await this.adminService.removeMember(this.appId, this.tenantId, this.teamId, memberId);
        this.toastService.show('Changes can take up to 5 minutes to reach running apps', 'info');
        await this.loadData();
      } catch (error) {
        this.toastService.show(getErrorMessage(error), 'error');
      }
    }
  }
}
