import { Injectable, OnDestroy, signal, computed, inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import type { BackgroundTask, TaskPoller } from '@PS/app-client';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class BackgroundTaskService implements OnDestroy {
  private poller: TaskPoller = inject('TASK_POLLER' as any);
  private tasksSignal = signal<readonly BackgroundTask[]>([]);

  readonly tasks = this.tasksSignal.asReadonly();
  readonly runningTasks = computed(() =>
    this.tasks().filter(t => t.status === 'Running')
  );
  readonly hasRunningTasks = computed(() => this.runningTasks().length > 0);
  readonly tasks$: Observable<readonly BackgroundTask[]>;

  constructor() {
    this.poller.subscribe((tasks: readonly BackgroundTask[]) => this.tasksSignal.set(tasks));
    this.tasks$ = toObservable(this.tasks);
  }

  async refresh(): Promise<void> {
    return this.poller.refresh();
  }

  expectTaskStart(): void {
    this.poller.expectTaskStart();
  }

  async createTask(
    taskType: string,
    taskData: unknown,
    description?: string,
    requiresNotification?: boolean
  ): Promise<string> {
    return this.poller.createTask(taskType, taskData, description, requiresNotification);
  }

  ngOnDestroy(): void {
    this.poller.stop();
  }
}
