import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AdminService } from '../../services/admin.service';
import { TeamSummary } from '../../services/models';
import { ToastService } from '../../shared/toast.service';
import { BreadcrumbComponent, BreadcrumbItem } from '../../shared/breadcrumb.component';
import { getErrorMessage } from '../../shared/error-messages';

@Component({
  selector: 'app-tenant-detail',
  standalone: true,
  imports: [CommonModule, RouterModule, BreadcrumbComponent],
  template: `
    <app-breadcrumb [items]="breadcrumbs()" />
    <h1>Tenant Details</h1>

    <button (click)="onCreateTeam()" class="btn-primary">Create Team</button>

    <div class="teams-table" style="margin-top: 20px;">
      @if (teams().length === 0) {
        <p>No teams found.</p>
      } @else {
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            @for (team of teams(); track team.id) {
              <tr (click)="navigateToTeam(team.id)" class="clickable">
                <td>
                  {{ team.name }}
                  @if (team.isDefault) {
                    <span class="badge badge-default">Default</span>
                  }
                </td>
                <td><span class="badge badge-enabled">Enabled</span></td>
                <td>
                  @if (!team.isDefault) {
                    <button (click)="onRenameTeam($event, team)" class="btn-sm">Rename</button>
                    <button (click)="onDeleteTeam($event, team)" class="btn-danger btn-sm">Delete</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`
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
    .btn-danger {
      background-color: #ef4444;
      color: white;
    }
    .btn-sm {
      padding: 4px 8px;
      font-size: 12px;
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
      margin-left: 8px;
    }
    .badge-default {
      background-color: #e0e7ff;
      color: #312e81;
    }
    .badge-enabled {
      background-color: #dcfce7;
      color: #166534;
    }
  `]
})
export class TenantDetailComponent implements OnInit {
  private adminService = inject(AdminService);
  private toastService = inject(ToastService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  appId = '';
  tenantId = '';
  teams = signal<TeamSummary[]>([]);
  breadcrumbs = signal<BreadcrumbItem[]>([]);

  async ngOnInit(): Promise<void> {
    this.appId = this.route.snapshot.paramMap.get('appId') || '';
    this.tenantId = this.route.snapshot.paramMap.get('tenantId') || '';
    this.breadcrumbs.set([
      { label: 'Applications', routerLink: ['/applications'] },
      { label: 'Application', routerLink: ['/applications', this.appId] },
      { label: 'Tenant' }
    ]);
    await this.loadTeams();
  }

  private async loadTeams(): Promise<void> {
    try {
      const teams = await this.adminService.getTeams(this.appId, this.tenantId);
      this.teams.set(teams);
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  onCreateTeam(): void {
    const name = prompt('Enter team name:');
    if (name) this.createTeam(name);
  }

  private async createTeam(name: string): Promise<void> {
    try {
      await this.adminService.createTeam(this.appId, this.tenantId, name);
      this.toastService.show('Team created', 'info');
      await this.loadTeams();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  onRenameTeam(event: Event, team: TeamSummary): void {
    event.stopPropagation();
    const newName = prompt('Enter new name:', team.name);
    if (newName && newName !== team.name) {
      this.renameTeam(team.id, newName);
    }
  }

  private async renameTeam(teamId: string, newName: string): Promise<void> {
    try {
      await this.adminService.patchTeam(this.appId, this.tenantId, teamId, newName);
      this.toastService.show('Changes can take up to 5 minutes to reach running apps', 'info');
      await this.loadTeams();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  onDeleteTeam(event: Event, team: TeamSummary): void {
    event.stopPropagation();
    if (confirm(`Delete team ${team.name}?`)) {
      this.deleteTeam(team.id);
    }
  }

  private async deleteTeam(teamId: string): Promise<void> {
    try {
      await this.adminService.deleteTeam(this.appId, this.tenantId, teamId);
      this.toastService.show('Changes can take up to 5 minutes to reach running apps', 'info');
      await this.loadTeams();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  navigateToTeam(teamId: string): void {
    this.router.navigate(['/applications', this.appId, 'tenants', this.tenantId, 'teams', teamId]);
  }
}
