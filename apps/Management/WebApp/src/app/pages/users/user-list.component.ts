import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { AdminService } from '../../services/admin.service';
import { ToastService } from '../../shared/toast.service';
import { getErrorMessage } from '../../shared/error-messages';
import { ApplicationSummary, RoleSummary, TeamSummary, TenantSummary, UserSummary } from '../../services/models';
import { InviteDialogComponent } from './invite-dialog.component';

@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, InviteDialogComponent],
  template: `
    <h2>Users</h2>

    <div class="toolbar">
      <div class="app-selector">
        <label>Application:</label>
        <select [(ngModel)]="selectedAppId" (change)="onAppChange()">
          <option value="">Select an application</option>
          @for (app of applications(); track app.id) {
            <option [value]="app.id">{{ app.displayName }}</option>
          }
        </select>
      </div>

      @if (selectedAppId()) {
        <input [(ngModel)]="searchTerm" placeholder="Search by name..." class="search-input" />

        <div class="filters">
          <button
            [class.active]="statusFilter() === 'all'"
            (click)="statusFilter.set('all')">
            All
          </button>
          <button
            [class.active]="statusFilter() === 'active'"
            (click)="statusFilter.set('active')">
            Active
          </button>
          <button
            [class.active]="statusFilter() === 'disabled'"
            (click)="statusFilter.set('disabled')">
            Disabled
          </button>
        </div>
      }
    </div>

    @if (showInviteDialog()) {
      <app-invite-dialog
        [url]="inviteUrl()"
        [expiresUtc]="inviteExpires()"
        (close)="showInviteDialog.set(false)" />
    }

    @if (selectedAppId()) {
      <div class="create-user-form">
        <h3>Create User</h3>
        <p class="form-help">Creating a user adds them to the team below and gives you their invitation link.</p>
        <div class="form-row">
          <input [(ngModel)]="newUserDisplayName" placeholder="Display Name" required />
          <input [(ngModel)]="newUserEmail" placeholder="Email (optional)" />
        </div>
        @if (tenants().length > 1) {
          <div class="form-row">
            <label>Tenant</label>
            <select [ngModel]="selectedTenantId()" (ngModelChange)="onTenantChange($event)">
              @for (tenant of tenants(); track tenant.id) {
                <option [value]="tenant.id">{{ tenant.name }}</option>
              }
            </select>
          </div>
        }
        @if (teams().length > 1) {
          <div class="form-row">
            <label>Team</label>
            <select [(ngModel)]="selectedTeamId">
              @for (team of teams(); track team.id) {
                <option [value]="team.id">{{ team.name }}{{ team.isDefault ? ' (default)' : '' }}</option>
              }
            </select>
          </div>
        }
        <div class="form-row roles-row">
          <label>Roles</label>
          @for (role of roles(); track role.id) {
            <label class="role-option">
              <input type="checkbox" [checked]="selectedRoleIds().includes(role.id)" (change)="toggleRole(role.id, $event)" />
              {{ role.displayName || role.name }}
            </label>
          }
          @if (roles().length === 0) {
            <span class="form-help">This application defines no roles.</span>
          }
        </div>
        <div class="form-row">
          <button (click)="createUser()" [disabled]="!newUserDisplayName() || !selectedTeamId() || creating()" class="btn-primary">
            Create and get invitation link
          </button>
        </div>
      </div>

      <div class="users-table">
        @if (filteredUsers().length === 0) {
          <p>No users found.</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>Display Name</th>
                <th>Email</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              @for (user of filteredUsers(); track user.id) {
                <tr (click)="selectUser(user.id)" class="clickable">
                  <td>{{ user.displayName }}</td>
                  <td>{{ user.email || '-' }}</td>
                  <td><span class="badge" [class.badge-enabled]="user.status === 'active'">{{ user.status }}</span></td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    }
  `,
  styles: [`
    .toolbar {
      display: flex;
      gap: 16px;
      align-items: center;
      margin: 20px 0;
      flex-wrap: wrap;
    }

    .app-selector, .filters {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    .app-selector select {
      padding: 8px;
      border: 1px solid #ddd;
      border-radius: 4px;
    }

    .search-input {
      padding: 8px;
      border: 1px solid #ddd;
      border-radius: 4px;
      flex: 1;
      min-width: 200px;
    }

    .filters button {
      padding: 6px 12px;
      border: 1px solid #ddd;
      background: white;
      cursor: pointer;
      border-radius: 4px;
    }

    .filters button.active {
      background: #2563eb;
      color: white;
      border-color: #2563eb;
    }

    .create-user-form {
      background: #f9fafb;
      padding: 16px;
      border-radius: 4px;
      margin: 20px 0;
    }

    .form-row {
      display: flex;
      gap: 8px;
      margin-top: 12px;
    }

    .form-row input {
      padding: 8px;
      border: 1px solid #ddd;
      border-radius: 4px;
      flex: 1;
    }

    .users-table {
      margin-top: 20px;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      background: white;
    }

    thead {
      background-color: #f3f4f6;
    }

    th, td {
      padding: 12px;
      text-align: left;
      border-bottom: 1px solid #e5e7eb;
    }

    .clickable {
      cursor: pointer;
    }

    .clickable:hover {
      background-color: #f9fafb;
    }

    .badge {
      display: inline-block;
      padding: 4px 8px;
      background-color: #e5e7eb;
      border-radius: 4px;
      font-size: 12px;
      color: #333;
    }

    .badge-enabled {
      background-color: #dcfce7;
      color: #166534;
    }

    .form-help {
      color: #6b7280;
      font-size: 14px;
      margin: 4px 0 12px;
    }

    .roles-row {
      align-items: center;
    }

    .role-option {
      display: inline-flex;
      gap: 6px;
      align-items: center;
    }

    .btn-primary {
      padding: 8px 16px;
      background-color: #2563eb;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
    }

    .btn-primary:disabled {
      background-color: #d1d5db;
      cursor: not-allowed;
    }
  `]
})
export class UserListComponent implements OnInit {
  private adminService = inject(AdminService);
  private toastService = inject(ToastService);
  private router = inject(Router);

  applications = signal<ApplicationSummary[]>([]);
  selectedAppId = signal<string>('');
  users = signal<UserSummary[]>([]);
  searchTerm = signal<string>('');
  statusFilter = signal<'all' | 'active' | 'disabled'>('all');

  newUserDisplayName = signal<string>('');
  newUserEmail = signal<string>('');

  // Where a new user goes: the application's tenant, team and roles.
  tenants = signal<TenantSummary[]>([]);
  teams = signal<TeamSummary[]>([]);
  roles = signal<RoleSummary[]>([]);
  selectedTenantId = signal<string>('');
  selectedTeamId = signal<string>('');
  selectedRoleIds = signal<string[]>([]);
  creating = signal<boolean>(false);

  showInviteDialog = signal<boolean>(false);
  inviteUrl = signal<string>('');
  inviteExpires = signal<string>('');

  filteredUsers = computed(() => {
    const term = this.searchTerm().toLowerCase();
    const status = this.statusFilter();
    return this.users().filter(u =>
      u.displayName.toLowerCase().includes(term) &&
      (status === 'all' || u.status === status)
    );
  });

  async ngOnInit(): Promise<void> {
    try {
      const apps = await this.adminService.getApplications();
      this.applications.set(apps);
      if (apps.length > 0) {
        this.selectedAppId.set(apps[0].id);
        await Promise.all([this.loadUsers(), this.loadCreateContext()]);
      }
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async onAppChange(): Promise<void> {
    if (this.selectedAppId()) {
      await Promise.all([this.loadUsers(), this.loadCreateContext()]);
    }
  }

  /** Loads the roles, tenants and teams a new user can be placed in. */
  private async loadCreateContext(): Promise<void> {
    try {
      const app = await this.adminService.getApplication(this.selectedAppId());
      this.roles.set(app.roles.filter(r => !r.isDeprecated));
      this.selectedRoleIds.set([]);
      const tenants = app.tenants.filter(t => !t.isDisabled);
      this.tenants.set(tenants);
      await this.onTenantChange(tenants[0]?.id ?? '');
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async onTenantChange(tenantId: string): Promise<void> {
    this.selectedTenantId.set(tenantId);
    this.teams.set([]);
    this.selectedTeamId.set('');
    if (!tenantId) return;
    try {
      const teams = await this.adminService.getTeams(this.selectedAppId(), tenantId);
      this.teams.set(teams);
      this.selectedTeamId.set((teams.find(t => t.isDefault) ?? teams[0])?.id ?? '');
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  toggleRole(roleId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.selectedRoleIds.update(ids =>
      checked ? [...ids, roleId] : ids.filter(id => id !== roleId));
  }

  private async loadUsers(): Promise<void> {
    try {
      const users = await this.adminService.getUsers(this.selectedAppId());
      this.users.set(users);
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async createUser(): Promise<void> {
    try {
      this.creating.set(true);
      const created = await this.adminService.createUser(this.selectedAppId(), {
        displayName: this.newUserDisplayName(),
        email: this.newUserEmail() || undefined,
        teamId: this.selectedTeamId(),
        roleIds: this.selectedRoleIds()
      });
      this.newUserDisplayName.set('');
      this.newUserEmail.set('');
      this.selectedRoleIds.set([]);
      this.inviteUrl.set(created.invitation.url);
      this.inviteExpires.set(new Date(created.invitation.expiresUtc).toLocaleString());
      this.showInviteDialog.set(true);
      await this.loadUsers();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    } finally {
      this.creating.set(false);
    }
  }

  selectUser(userId: string): void {
    this.router.navigate(['/users', userId], {
      queryParams: { appId: this.selectedAppId() }
    });
  }
}
