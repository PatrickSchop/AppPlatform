import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: string;
  message: string;
  type: 'info' | 'error' | 'warning';
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  toasts = signal<Toast[]>([]);

  show(message: string, type: 'info' | 'error' | 'warning' = 'info'): void {
    const id = Date.now().toString();
    const toast: Toast = { id, message, type };
    this.toasts.update(t => [...t, toast]);

    // Auto-dismiss after 5 seconds
    setTimeout(() => {
      this.toasts.update(t => t.filter(x => x.id !== id));
    }, 5000);
  }

  remove(id: string): void {
    this.toasts.update(t => t.filter(x => x.id !== id));
  }
}
