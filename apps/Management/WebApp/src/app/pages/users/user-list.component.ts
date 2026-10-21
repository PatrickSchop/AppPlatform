import { Component, inject, OnInit, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { AdminService } from '../../services/admin.service';
import { ToastService } from '../../shared/toast.service';
import { getErrorMessage } from '../../shared/error-messages';
import { ApplicationSummary, UserSummary } from '../../services/models';

@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
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

    @if (selectedAppId()) {
      <div class="create-user-form">
        <h3>Create User</h3>
        <div class="form-row">
          <input [(ngModel)]="newUserDisplayName" placeholder="Display Name" required />
          <input [(ngModel)]="newUserEmail" placeholder="Email (optional)" />
          <button (click)="createUser()" [disabled]="!newUserDisplayName()" class="btn-primary">Create</button>
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
        await this.loadUsers();
      }
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async onAppChange(): Promise<void> {
    if (this.selectedAppId()) {
      await this.loadUsers();
    }
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
      const displayName = this.newUserDisplayName();
      const email = this.newUserEmail() || undefined;

      await this.adminService.createUser(this.selectedAppId(), displayName, email);
      this.newUserDisplayName.set('');
      this.newUserEmail.set('');
      this.toastService.show('User created successfully', 'info');
      await this.loadUsers();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  selectUser(userId: string): void {
    this.router.navigate(['/users', userId], {
      queryParams: { appId: this.selectedAppId() }
    });
  }
}
