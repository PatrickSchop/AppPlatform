import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { BackgroundTaskService } from '@PS/app-client-angular';
import { ExampleItem, ExampleService, EXAMPLE_TASK_TYPE } from '../../services/example.service';
import { ToastService } from '../../shared/toast.service';
import { getErrorMessage } from '../../shared/error-messages';

@Component({
  selector: 'app-example',
  standalone: true,
  template: `
    <h2>Example</h2>
    <p class="intro">
      A list loaded through the platform's API client, and a background task whose progress shows in
      the top bar. Copy this page to start your own.
    </p>

    <div class="toolbar">
      <button class="btn-primary" (click)="startTask()" [disabled]="starting()">Start background task</button>
    </div>

    <div class="items-table">
      @if (loading()) {
        <p>Loading...</p>
      } @else if (items().length === 0) {
        <p>No items found.</p>
      } @else {
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Created</th>
            </tr>
          </thead>
          <tbody>
            @for (item of items(); track item.id) {
              <tr>
                <td>{{ item.title ?? item.name }}</td>
                <td>{{ item.createdUtc ? (item.createdUtc | date: 'medium') : '' }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  imports: [DatePipe],
  styles: [`
    .intro {
      color: var(--color-text-muted);
    }
    .toolbar {
      margin: 1rem 0;
    }
  `]
})
export class ExampleComponent implements OnInit {
  private example = inject(ExampleService);
  private tasks = inject(BackgroundTaskService);
  private toast = inject(ToastService);
  private router = inject(Router);

  items = signal<ExampleItem[]>([]);
  loading = signal(true);
  starting = signal(false);

  async ngOnInit(): Promise<void> {
    try {
      this.items.set(await this.example.getItems());
    } catch (error) {
      this.fail(error);
    } finally {
      this.loading.set(false);
    }
  }

  async startTask(): Promise<void> {
    this.starting.set(true);
    try {
      await this.tasks.createTask(EXAMPLE_TASK_TYPE, {}, 'Example task', true);
    } catch (error) {
      this.fail(error);
    } finally {
      this.starting.set(false);
    }
  }

  /** A 403 means signed in but not allowed: that has its own page. Anything else is a toast. */
  private fail(error: unknown): void {
    if ((error as { status?: number })?.status === 403) {
      void this.router.navigate(['/forbidden'], { queryParams: { reason: 'role' } });
    } else {
      this.toast.show(getErrorMessage(error), 'error');
    }
  }
}
