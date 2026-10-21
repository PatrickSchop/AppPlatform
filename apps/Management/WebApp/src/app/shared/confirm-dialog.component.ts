import { Component, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="modal-overlay" (click)="onCancel()">
      <div class="modal-content" (click)="$event.stopPropagation()">
        <h2>{{ title() }}</h2>
        <p>{{ message() }}</p>
        <div class="modal-buttons">
          <button (click)="onCancel()" class="btn-cancel">{{ cancelLabel() }}</button>
          <button (click)="onConfirm()" class="btn-confirm">{{ confirmLabel() }}</button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .modal-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background-color: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 100;
    }
    .modal-content {
      background: white;
      padding: 20px;
      border-radius: 8px;
      max-width: 400px;
      box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
    }
    h2 {
      margin-top: 0;
      margin-bottom: 10px;
    }
    p {
      margin: 10px 0 20px;
      color: #666;
    }
    .modal-buttons {
      display: flex;
      gap: 10px;
      justify-content: flex-end;
    }
    button {
      padding: 8px 16px;
      border: none;
      border-radius: 4px;
      cursor: pointer;
      font-size: 14px;
    }
    .btn-cancel {
      background-color: #e5e7eb;
      color: #333;
    }
    .btn-cancel:hover {
      background-color: #d1d5db;
    }
    .btn-confirm {
      background-color: #ef4444;
      color: white;
    }
    .btn-confirm:hover {
      background-color: #dc2626;
    }
  `]
})
export class ConfirmDialogComponent {
  title = input<string>('Confirm');
  message = input<string>('Are you sure?');
  confirmLabel = input<string>('Delete');
  cancelLabel = input<string>('Cancel');

  confirmed = output<void>();
  cancelled = output<void>();

  onConfirm(): void {
    this.confirmed.emit();
  }

  onCancel(): void {
    this.cancelled.emit();
  }
}
