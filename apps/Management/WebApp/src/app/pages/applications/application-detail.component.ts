import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AdminService } from '../../services/admin.service';
import { ApplicationDetail, RoleSummary, TenantSummary } from '../../services/models';
import { ToastService } from '../../shared/toast.service';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';
import { BreadcrumbComponent, BreadcrumbItem } from '../../shared/breadcrumb.component';
import { getErrorMessage } from '../../shared/error-messages';

@Component({
  selector: 'app-application-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, ConfirmDialogComponent, BreadcrumbComponent],
  template: `
    <app-breadcrumb [items]="breadcrumbs()" />
    <h1>{{ application()?.displayName }}</h1>

    <div class="tabs">
      <button [class.active]="activeTab() === 'settings'" (click)="activeTab.set('settings')">Settings</button>
      <button [class.active]="activeTab() === 'roles'" (click)="activeTab.set('roles')">Roles</button>
      <button [class.active]="activeTab() === 'tenants'" (click)="activeTab.set('tenants')">Tenants</button>
    </div>

    @if (activeTab() === 'settings') {
      <div class="tab-content">
        <div class="form-group">
          <label>Name</label>
          <input [(ngModel)]="editName" type="text" />
        </div>
        <div class="form-group">
          <label>Key</label>
          <code>{{ application()?.key }}</code>
        </div>
        <div class="form-group">
          <label>Tenancy</label>
          <span class="badge">{{ application()?.tenancy }}</span>
        </div>
        <div class="form-group">
          <label>Disabled</label>
          <input [(ngModel)]="editDisabled" type="checkbox" />
        </div>
        <button (click)="saveSettings()" [disabled]="!editName" class="btn-primary">Save</button>
      </div>
    }

    @if (activeTab() === 'roles') {
      <div class="tab-content">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Display Name</th>
              <th>Status</th>
              <th>Assignments</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            @for (role of application()?.roles || []; track role.id) {
              <tr>
                <td>{{ role.name }}</td>
                <td>{{ role.displayName }}</td>
                <td>
                  @if (role.isDeprecated) {
                    <span class="badge badge-deprecated">Deprecated</span>
                  }
                </td>
                <td>{{ role.assignmentCount }}</td>
                <td>
                  @if (role.isDeprecated) {
                    <button (click)="onDeleteRole(role)" class="btn-danger btn-sm">Delete</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (activeTab() === 'tenants') {
      <div class="tab-content">
        @if (isSingleTenant()) {
          <p>This application uses a single default tenant.</p>
        } @else {
          <button (click)="onCreateTenant()" class="btn-primary">Create Tenant</button>
        }
        <table style="margin-top: 20px;">
          <thead>
            <tr>
              <th>Name</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            @for (tenant of tenants(); track tenant.id) {
              <tr (click)="navigateToTenant(tenant.id)" class="clickable">
                <td>{{ tenant.name }}</td>
                <td>
                  <span class="badge" [class.badge-enabled]="!tenant.isDisabled">{{ tenant.isDisabled ? 'Disabled' : 'Enabled' }}</span>
                </td>
                <td>
                  @if (!isSingleTenant()) {
                    <button (click)="onDeleteTenant($event, tenant)" class="btn-danger btn-sm">Delete</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }

    @if (showDeleteConfirm()) {
      <app-confirm-dialog
        title="Delete Role"
        message="Are you sure?"
        confirmLabel="Delete"
        (confirmed)="confirmDeleteRole()"
        (cancelled)="showDeleteConfirm.set(false)"
      />
    }

    @if (showDeleteTenantConfirm()) {
      <app-confirm-dialog
        title="Delete Tenant"
        message="Delete tenant and all its teams?"
        confirmLabel="Delete"
        (confirmed)="confirmDeleteTenant()"
        (cancelled)="showDeleteTenantConfirm.set(false)"
      />
    }
  `,
  styles: [`
    .tabs {
      display: flex;
      gap: 10px;
      margin: 20px 0;
      border-bottom: 1px solid #e5e7eb;
    }
    .tabs button {
      padding: 10px 16px;
      background: none;
      border: none;
      cursor: pointer;
      border-bottom: 2px solid transparent;
      color: #666;
    }
    .tabs button.active {
      color: #3b82f6;
      border-bottom-color: #3b82f6;
    }
    .tab-content {
      margin-top: 20px;
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
      max-width: 400px;
      padding: 8px;
      border: 1px solid #d1d5db;
      border-radius: 4px;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      background: white;
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
      border-radius: 4px;
      font-size: 12px;
    }
    .badge-enabled {
      background-color: #dcfce7;
      color: #166534;
    }
    .badge-deprecated {
      background-color: #fef3c7;
      color: #92400e;
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
export class ApplicationDetailComponent implements OnInit {
  private adminService = inject(AdminService);
  private toastService = inject(ToastService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  appId = '';
  application = signal<ApplicationDetail | null>(null);
  tenants = signal<TenantSummary[]>([]);
  activeTab = signal<'settings' | 'roles' | 'tenants'>('settings');

  editName = '';
  editDisabled = false;

  showDeleteConfirm = signal(false);
  showDeleteTenantConfirm = signal(false);
  roleToDelete: RoleSummary | null = null;
  tenantToDelete: TenantSummary | null = null;

  breadcrumbs = signal<BreadcrumbItem[]>([]);

  async ngOnInit(): Promise<void> {
    this.appId = this.route.snapshot.paramMap.get('appId') || '';
    await this.loadApplication();
  }

  private async loadApplication(): Promise<void> {
    try {
      const app = await this.adminService.getApplication(this.appId);
      this.application.set(app);
      this.editName = app.displayName;
      this.editDisabled = app.isDisabled;
      this.breadcrumbs.set([
        { label: 'Applications', routerLink: ['/applications'] },
        { label: app.displayName }
      ]);
      await this.loadTenants();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  private async loadTenants(): Promise<void> {
    try {
      const tenants = await this.adminService.getTenants(this.appId);
      this.tenants.set(tenants);
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async saveSettings(): Promise<void> {
    try {
      await this.adminService.patchApplication(this.appId, {
        displayName: this.editName,
        isDisabled: this.editDisabled
      });
      this.toastService.show('Changes can take up to 5 minutes to reach running apps', 'info');
      await this.loadApplication();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  onDeleteRole(role: RoleSummary): void {
    this.roleToDelete = role;
    this.showDeleteConfirm.set(true);
  }

  async confirmDeleteRole(): Promise<void> {
    if (!this.roleToDelete) return;
    try {
      await this.adminService.deleteRole(this.appId, this.roleToDelete.id);
      this.showDeleteConfirm.set(false);
      this.toastService.show('Changes can take up to 5 minutes to reach running apps', 'info');
      await this.loadApplication();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  onCreateTenant(): void {
    const name = prompt('Enter tenant name:');
    if (name) this.createTenant(name);
  }

  private async createTenant(name: string): Promise<void> {
    try {
      await this.adminService.createTenant(this.appId, name);
      this.toastService.show('Tenant created', 'info');
      await this.loadTenants();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  onDeleteTenant(event: Event, tenant: TenantSummary): void {
    event.stopPropagation();
    this.tenantToDelete = tenant;
    this.showDeleteTenantConfirm.set(true);
  }

  async confirmDeleteTenant(): Promise<void> {
    if (!this.tenantToDelete) return;
    try {
      await this.adminService.deleteTenant(this.appId, this.tenantToDelete.id);
      this.showDeleteTenantConfirm.set(false);
      this.toastService.show('Changes can take up to 5 minutes to reach running apps', 'info');
      await this.loadTenants();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  navigateToTenant(tenantId: string): void {
    this.router.navigate(['/applications', this.appId, 'tenants', tenantId]);
  }

  isSingleTenant(): boolean {
    return this.application()?.tenancy === 'Single';
  }
}
