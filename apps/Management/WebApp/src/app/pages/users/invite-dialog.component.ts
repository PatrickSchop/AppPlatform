import { Component, Input, Output, EventEmitter, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../shared/toast.service';

@Component({
  selector: 'app-invite-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="dialog-overlay" (click)="close.emit()">
      <div class="dialog-content" (click)="$event.stopPropagation()">
        <h3>Invitation Link</h3>

        <div class="warning-box">
          ⚠️ This link will not be shown again. Anyone who opens it first can claim this user.
        </div>

        <div class="url-section">
          <label>Invitation URL:</label>
          <div class="url-input-wrapper">
            <input [(ngModel)]="urlDisplay" type="text" readonly />
            <button (click)="copyToClipboard()" class="btn-copy">Copy</button>
          </div>
        </div>

        <div class="info-section">
          <p><strong>Expires:</strong> {{ expiresUtc }}</p>
          <p><strong>Note:</strong> Creating a new link revokes the old one.</p>
        </div>

        <div class="dialog-actions">
          <button (click)="close.emit()" class="btn-primary">Close</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .dialog-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .dialog-content {
      background: white;
      border-radius: 8px;
      padding: 24px;
      max-width: 500px;
      width: 90%;
      box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
    }

    h3 {
      margin-top: 0;
      margin-bottom: 16px;
    }

    .warning-box {
      background: #fef3c7;
      border: 1px solid #fcd34d;
      color: #92400e;
      padding: 12px;
      border-radius: 4px;
      margin-bottom: 20px;
    }

    .url-section {
      margin-bottom: 20px;
    }

    .url-section label {
      display: block;
      margin-bottom: 8px;
      font-weight: 500;
    }

    .url-input-wrapper {
      display: flex;
      gap: 8px;
    }

    .url-input-wrapper input {
      flex: 1;
      padding: 8px;
      border: 1px solid #ddd;
      border-radius: 4px;
      font-family: monospace;
      font-size: 12px;
    }

    .btn-copy {
      padding: 8px 16px;
      background-color: #10b981;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      white-space: nowrap;
    }

    .btn-copy:hover {
      background-color: #059669;
    }

    .info-section {
      background: #f9fafb;
      padding: 12px;
      border-radius: 4px;
      margin-bottom: 20px;
      font-size: 14px;
    }

    .info-section p {
      margin: 8px 0;
    }

    .dialog-actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
    }

    .btn-primary {
      padding: 8px 16px;
      background-color: #2563eb;
      color: white;
      border: none;
      border-radius: 4px;
      cursor: pointer;
    }

    .btn-primary:hover {
      background-color: #1d4ed8;
    }
  `]
})
export class InviteDialogComponent {
  @Input() url: string = '';
  @Input() expiresUtc: string = '';
  @Output() close = new EventEmitter<void>();

  private toastService = inject(ToastService);
  urlDisplay = signal<string>('');

  ngOnInit(): void {
    this.urlDisplay.set(this.url);
  }

  async copyToClipboard(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.urlDisplay());
      this.toastService.show('Copied to clipboard', 'info');
    } catch (error) {
      this.toastService.show('Failed to copy', 'error');
    }
  }
}
