import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AdminService } from '../../services/admin.service';
import { ToastService } from '../../shared/toast.service';
import { getErrorMessage } from '../../shared/error-messages';
import { AUTH_CLIENT } from '@patrickschop/app-client-angular';
import { InvitationPreviewResponse } from '../../services/models';

@Component({
  selector: 'app-accept-invite',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (loading()) {
      <div class="loading">
        <p>Loading invitation...</p>
      </div>
    } @else if (error()) {
      <div class="error-container">
        <h2>Invitation Error</h2>
        <p class="error-message">{{ error() }}</p>
        <button (click)="goBack()" class="btn-primary">Go Back</button>
      </div>
    } @else if (accepted()) {
      <div class="success-container">
        <h2>Invitation Accepted</h2>
        <p>You now have access to the following applications:</p>
        <ul>
          @for (app of applications(); track app) {
            <li>{{ app }}</li>
          }
        </ul>
        <button (click)="goToApplications()" class="btn-primary">Go to Applications</button>
      </div>
    } @else if (preview()) {
      <div class="preview-container">
        <h2>Accept Invitation</h2>
        <div class="preview-info">
          <p><strong>For:</strong> {{ preview()?.displayName || 'User' }}</p>
          <p><strong>Applications:</strong></p>
          <ul>
            @for (app of previewApps(); track app) {
              <li>{{ app }}</li>
            }
          </ul>
        </div>
        <button (click)="acceptInvitation()" class="btn-primary">Accept Invitation</button>
        <button (click)="decline()" class="btn-secondary">Decline</button>
      </div>
    }
  `,
  styles: [`
    .loading, .error-container, .success-container, .preview-container {
      padding: 40px 20px;
      text-align: center;
      max-width: 600px;
      margin: 0 auto;
    }

    .error-message {
      background: #fee;
      border: 1px solid #fcc;
      color: #c33;
      padding: 12px;
      border-radius: 4px;
      margin: 20px 0;
    }

    .preview-info {
      background: #f9fafb;
      padding: 20px;
      border-radius: 4px;
      text-align: left;
      margin: 20px 0;
    }

    .preview-info ul, .success-container ul {
      margin: 12px 0;
      padding-left: 20px;
    }

    .preview-info li, .success-container li {
      margin: 8px 0;
    }

    .btn-primary {
      padding: 10px 24px;
      background-color: #2563eb;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      margin: 8px;
    }

    .btn-primary:hover {
      background-color: #1d4ed8;
    }

    .btn-secondary {
      padding: 10px 24px;
      background-color: #6b7280;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      margin: 8px;
    }

    .btn-secondary:hover {
      background-color: #4b5563;
    }
  `]
})
export class AcceptInviteComponent implements OnInit {
  private adminService = inject(AdminService);
  private toastService = inject(ToastService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private authClient = inject(AUTH_CLIENT);

  loading = signal(true);
  error = signal<string>('');
  preview = signal<InvitationPreviewResponse | null>(null);
  accepted = signal(false);
  previewApps = signal<string[]>([]);
  applications = signal<string[]>([]);

  private token: string = '';

  async ngOnInit(): Promise<void> {
    this.token = this.route.snapshot.paramMap.get('token') || '';

    if (!this.token) {
      this.error.set('Invalid invitation link');
      this.loading.set(false);
      return;
    }

    try {
      // The invitee is not registered yet (that is what accepting does), so "signed in" means
      // an identity-provider session, not a registry user. Sign-in returns to this page.
      if (!this.authClient?.getAccount()) {
        await this.authClient?.signIn();
        return;
      }

      // Load invitation preview
      await this.loadPreview();
    } catch (error) {
      this.handleError(error);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadPreview(): Promise<void> {
    try {
      const response = await this.adminService.previewInvitation(this.token);
      this.preview.set(response);

      this.previewApps.set(response.applications);
    } catch (error) {
      this.handleError(error);
    }
  }

  async acceptInvitation(): Promise<void> {
    this.loading.set(true);
    try {
      await this.adminService.acceptInvitation(this.token);

      // A non-2xx response throws, so reaching here means the invitation was accepted.
      // Role changes can take a minute to be visible, so do not decide what to show from
      // a fresh role lookup; show what the invitation granted.
      this.applications.set(this.previewApps());
      this.accepted.set(true);
    } catch (error) {
      this.handleError(error);
    } finally {
      this.loading.set(false);
    }
  }

  private handleError(error: any): void {
    let message = getErrorMessage(error);

    if (error?.status === 410) {
      message = 'This invitation has expired or has already been used';
    } else if (error?.status === 409) {
      const code = error?.body?.error;
      if (code === 'identity_already_registered') {
        message = 'Your identity is already registered in the system';
      } else if (code === 'already_bound') {
        message = 'This user account is already linked to an identity';
      }
    }

    this.error.set(message);
  }

  decline(): void {
    this.router.navigate(['/']);
  }

  goBack(): void {
    this.router.navigate(['/']);
  }

  goToApplications(): void {
    this.router.navigate(['/applications']);
  }
}
