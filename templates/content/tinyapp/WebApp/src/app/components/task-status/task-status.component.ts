import { Component, ElementRef, HostListener, inject, signal } from '@angular/core';
import { BackgroundTaskService, TaskProgressComponent } from '@PS/app-client-angular';

/**
 * A top-bar indicator for background tasks. Hidden while there are none; a click expands the
 * per-task progress bars. The list and polling live in BackgroundTaskService.
 */
@Component({
  selector: 'app-task-status',
  standalone: true,
  imports: [TaskProgressComponent],
  template: `
    @if (tasks.tasks().length > 0) {
      <div class="task-status">
        <button
          type="button"
          class="task-indicator"
          [class.running]="tasks.hasRunningTasks()"
          [attr.aria-expanded]="open()"
          (click)="open.set(!open())">
          @if (tasks.hasRunningTasks()) {
            <span class="spinner" aria-hidden="true"></span>
            {{ tasks.runningTasks().length }} running
          } @else {
            Tasks
          }
        </button>
        @if (open()) {
          <div class="task-popover" role="dialog" aria-label="Background tasks">
            <PS-task-progress />
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .task-status {
      position: relative;
    }
    .task-indicator {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      background: transparent;
      color: var(--color-text-muted);
      border: 1px solid var(--color-border);
      border-radius: 0.375rem;
      padding: 0.5rem 1rem;
      font-size: 0.875rem;
      cursor: pointer;

      &.running {
        color: var(--color-text);
        border-color: var(--color-primary);
      }
    }
    .spinner {
      width: 0.75rem;
      height: 0.75rem;
      border: 2px solid var(--color-border);
      border-top-color: var(--color-primary);
      border-radius: 50%;
      animation: spin 1s linear infinite;
    }
    @keyframes spin {
      to { transform: rotate(360deg); }
    }
    .task-popover {
      position: absolute;
      right: 0;
      top: calc(100% + 0.5rem);
      z-index: 100;
      width: 22rem;
      max-height: 24rem;
      overflow-y: auto;
      background: var(--color-bg);
      border: 1px solid var(--color-border);
      border-radius: 0.5rem;
      box-shadow: 0 10px 25px rgba(0, 0, 0, 0.15);
      padding: 0.75rem;
    }
    @media (prefers-reduced-motion: reduce) {
      .spinner { animation: none; }
    }
  `]
})
export class TaskStatusComponent {
  protected tasks = inject(BackgroundTaskService);
  private host = inject(ElementRef<HTMLElement>);

  open = signal(false);

  @HostListener('document:click', ['$event'])
  closeOnOutsideClick(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target)) {
      this.open.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  closeOnEscape(): void {
    this.open.set(false);
  }
}
