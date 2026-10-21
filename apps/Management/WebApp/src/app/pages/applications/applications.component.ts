import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { AdminService } from '../../services/admin.service';
import { ApplicationSummary } from '../../services/models';
import { ToastService } from '../../shared/toast.service';
import { getErrorMessage } from '../../shared/error-messages';

@Component({
  selector: 'app-applications',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <h2>Applications</h2>
    <div class="applications-table">
      @if (applications().length === 0) {
        <p>No applications found.</p>
      } @else {
        <table>
          <thead>
            <tr>
              <th>Key</th>
              <th>Name</th>
              <th>Tenancy</th>
              <th>Tenants</th>
              <th>Users</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            @for (app of applications(); track app.id) {
              <tr (click)="navigate(app.id)" class="clickable">
                <td>{{ app.id }}</td>
                <td>{{ app.name }}</td>
                <td><span class="badge">{{ getTenancyType(app.id) }}</span></td>
                <td>-</td>
                <td>-</td>
                <td><span class="badge badge-enabled">Enabled</span></td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`
    .applications-table {
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
  `]
})
export class ApplicationListComponent implements OnInit {
  private adminService = inject(AdminService);
  private toastService = inject(ToastService);

  applications = signal<ApplicationSummary[]>([]);

  async ngOnInit(): Promise<void> {
    try {
      const apps = await this.adminService.getApplications();
      this.applications.set(apps);
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  navigate(appId: string): void {
    // Router navigation will be handled by the route configuration
  }

  getTenancyType(appId: string): string {
    // This would be populated from application details in a real implementation
    return 'Multi';
  }
}
