import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AdminService } from '../../services/admin.service';
import { ToastService } from '../../shared/toast.service';
import { getErrorMessage } from '../../shared/error-messages';
import { UserDetail, InvitationSummary } from '../../services/models';
import { InviteDialogComponent } from './invite-dialog.component';

@Component({
  selector: 'app-user-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, InviteDialogComponent],
  template: `
    <h2>{{ user()?.displayName }}</h2>

    <div class="tabs">
      <button [class.active]="activeTab() === 'profile'" (click)="activeTab.set('profile')">Profile</button>
      <button [class.active]="activeTab() === 'memberships'" (click)="activeTab.set('memberships')">Memberships</button>
      <button [class.active]="activeTab() === 'invitations'" (click)="activeTab.set('invitations')">Invitations</button>
    </div>

    @if (activeTab() === 'profile') {
      <div class="tab-content">
        <div class="form-group">
          <label>Display Name</label>
          <input [(ngModel)]="editDisplayName" type="text" />
          <button (click)="saveProfile()" class="btn-primary">Save</button>
        </div>
        <div class="form-group">
          <label>Email</label>
          <input [(ngModel)]="editEmail" type="text" />
          <button (click)="saveProfile()" class="btn-primary">Save</button>
        </div>
      </div>
    }

    @if (activeTab() === 'memberships') {
      <div class="tab-content">
        @if (!user()?.memberships || user()!.memberships.length === 0) {
          <p>No memberships found.</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>Application</th>
                <th>Tenant</th>
                <th>Team</th>
                <th>Roles</th>
              </tr>
            </thead>
            <tbody>
              @for (membership of user()?.memberships || []; track membership.memberId) {
                <tr>
                  <td>{{ membership.appKey }}</td>
                  <td>{{ membership.tenantName }}</td>
                  <td>{{ membership.teamName }}</td>
                  <td>{{ membership.roles.length ? membership.roles.join(', ') : 'No role' }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    }

    @if (activeTab() === 'invitations') {
      <div class="tab-content">
        <button (click)="createInvite()" class="btn-primary">Create Invite Link</button>

        @if (!invitations() || invitations().length === 0) {
          <p style="margin-top: 20px;">No invitations.</p>
        } @else {
          <table style="margin-top: 20px;">
            <thead>
              <tr>
                <th>Expires</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              @for (inv of invitations(); track inv.id) {
                <tr>
                  <td>{{ formatDate(inv.expiresUtc) }}</td>
                  <td><span class="badge">{{ inv.status }}</span></td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    }

    @if (showInviteDialog()) {
      <app-invite-dialog
        [url]="inviteUrl()"
        [expiresUtc]="inviteExpires()"
        (close)="showInviteDialog.set(false)" />
    }
  `,
  styles: [`
    .tabs {
      display: flex;
      gap: 12px;
      margin: 20px 0;
      border-bottom: 1px solid #e5e7eb;
    }

    .tabs button {
      padding: 10px 16px;
      border: none;
      background: transparent;
      cursor: pointer;
      border-bottom: 2px solid transparent;
      color: #6b7280;
    }

    .tabs button.active {
      border-bottom-color: #2563eb;
      color: #2563eb;
    }

    .tab-content {
      padding: 20px 0;
    }

    .form-group {
      margin-bottom: 20px;
    }

    .form-group label {
      display: block;
      margin-bottom: 8px;
      font-weight: 500;
    }

    .form-group input {
      padding: 8px;
      border: 1px solid #ddd;
      border-radius: 4px;
      width: 300px;
    }

    .btn-primary {
      padding: 8px 16px;
      background-color: #2563eb;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      margin-left: 8px;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      background: white;
      margin-top: 20px;
    }

    thead {
      background-color: #f3f4f6;
    }

    th, td {
      padding: 12px;
      text-align: left;
      border-bottom: 1px solid #e5e7eb;
    }

    .badge {
      display: inline-block;
      padding: 4px 8px;
      background-color: #e5e7eb;
      border-radius: 4px;
      font-size: 12px;
      color: #333;
    }
  `]
})
export class UserDetailComponent implements OnInit {
  private adminService = inject(AdminService);
  private toastService = inject(ToastService);
  private route = inject(ActivatedRoute);

  user = signal<UserDetail | null>(null);
  invitations = signal<InvitationSummary[]>([]);
  activeTab = signal<'profile' | 'memberships' | 'invitations'>('profile');
  showInviteDialog = signal<boolean>(false);
  inviteUrl = signal<string>('');
  inviteExpires = signal<string>('');

  editDisplayName = signal<string>('');
  editEmail = signal<string>('');

  private userId: string = '';
  private appId: string = '';

  async ngOnInit(): Promise<void> {
    this.userId = this.route.snapshot.paramMap.get('userId') || '';
    this.appId = this.route.snapshot.queryParamMap.get('appId') || '';

    if (this.userId && this.appId) {
      await this.loadUser();
      await this.loadInvitations();
    }
  }

  private async loadUser(): Promise<void> {
    try {
      const user = await this.adminService.getUser(this.appId, this.userId);
      this.user.set(user);
      this.editDisplayName.set(user.displayName);
      this.editEmail.set(user.email || '');
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  private async loadInvitations(): Promise<void> {
    try {
      const invitations = await this.adminService.getInvitations(this.userId);
      this.invitations.set(invitations);
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async saveProfile(): Promise<void> {
    try {
      const displayName = this.editDisplayName();
      const email = this.editEmail() || undefined;

      await this.adminService.patchUser(this.appId, this.userId, {
        displayName,
        email
      });
      this.toastService.show('Profile updated successfully', 'info');
      await this.loadUser();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  async createInvite(): Promise<void> {
    try {
      const response = await this.adminService.createInvitation(this.userId);
      this.inviteUrl.set(response.url);
      this.inviteExpires.set(new Date(response.expiresUtc).toLocaleString());
      this.showInviteDialog.set(true);
      await this.loadInvitations();
    } catch (error) {
      this.toastService.show(getErrorMessage(error), 'error');
    }
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString();
  }
}
