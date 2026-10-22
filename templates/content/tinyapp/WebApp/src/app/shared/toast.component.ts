import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from './toast.service';

@Component({
  selector: 'app-toast',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="toast-container">
      @for (toast of toastService.toasts(); track toast.id) {
        <div [ngClass]="'toast toast-' + toast.type">
          {{ toast.message }}
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-container {
      position: fixed;
      top: 20px;
      right: 20px;
      z-index: 1000;
      display: flex;
      flex-direction: column;
      gap: 10px;
    }
    .toast {
      padding: 12px 20px;
      border-radius: 4px;
      color: white;
      min-width: 300px;
    }
    .toast-info {
      background-color: #3b82f6;
    }
    .toast-error {
      background-color: #ef4444;
    }
    .toast-warning {
      background-color: #f59e0b;
    }
  `]
})
export class ToastComponent {
  toastService = inject(ToastService);
}
