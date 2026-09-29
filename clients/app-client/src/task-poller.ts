import { ApiClient } from './api-client.js';

export interface BackgroundTask {
  id: string;
  taskType: string;
  status: 'New' | 'Resumed' | 'NotStarted' | 'Running' | 'Paused' | 'Completed' | 'Failed';
  statusMessage: string;
  completionPercentage: number;
  description: string;
  requiresNotification: boolean;
  createdDate: string;
  updatedDate: string;
  startedDate: string | null;
  completedDate: string | null;
}

export interface TaskPollerOptions {
  api: ApiClient;
  idleIntervalMs?: number;
  activeIntervalMs?: number;
  expectTaskStartMs?: number;
  autoStart?: boolean;
}

export class TaskPoller extends EventTarget {
  private api: ApiClient;
  private idleIntervalMs: number;
  private activeIntervalMs: number;
  private expectTaskStartMs: number;
  private _tasks: BackgroundTask[] = [];
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private pollInFlight = false;
  private errorCount = 0;
  private currentIntervalMs: number;
  private expectTaskStartTimer: ReturnType<typeof setTimeout> | null = null;
  private isVisible = true;

  readonly tasks!: readonly BackgroundTask[];

  constructor(options: TaskPollerOptions) {
    super();
    this.api = options.api;
    this.idleIntervalMs = options.idleIntervalMs ?? 30000;
    this.activeIntervalMs = options.activeIntervalMs ?? 1000;
    this.expectTaskStartMs = options.expectTaskStartMs ?? 10000;
    this.currentIntervalMs = this.idleIntervalMs;

    Object.defineProperty(this, 'tasks', {
      get: () => this._tasks,
      enumerable: true,
    });

    if (options.autoStart !== false) {
      this.start();
    }

    document.addEventListener('visibilitychange', () => this.handleVisibilityChange());
  }

  start(): void {
    if (this.pollTimer !== null) return;
    this.schedulePoll();
  }

  stop(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
    if (this.expectTaskStartTimer !== null) {
      clearTimeout(this.expectTaskStartTimer);
      this.expectTaskStartTimer = null;
    }
  }

  async refresh(): Promise<void> {
    await this.poll();
  }

  expectTaskStart(): void {
    this.currentIntervalMs = this.activeIntervalMs;
    if (this.expectTaskStartTimer !== null) {
      clearTimeout(this.expectTaskStartTimer);
    }
    this.expectTaskStartTimer = setTimeout(() => {
      this.updatePollingInterval();
      this.expectTaskStartTimer = null;
    }, this.expectTaskStartMs);
  }

  async createTask(
    taskType: string,
    taskData: unknown,
    description = '',
    requiresNotification = false
  ): Promise<string> {
    const response = await this.api.post<{ id: string }>('/api/tasks', {
      taskType,
      taskData,
      description,
      requiresNotification,
    });
    this.expectTaskStart();
    return response.id;
  }

  subscribe(listener: (tasks: readonly BackgroundTask[]) => void): () => void {
    const handler = (event: Event) => {
      if (event instanceof CustomEvent) {
        listener(event.detail);
      }
    };
    this.addEventListener('tasks', handler);
    return () => this.removeEventListener('tasks', handler);
  }

  private async poll(): Promise<void> {
    if (this.pollInFlight) return;
    this.pollInFlight = true;

    try {
      const tasks = await this.api.get<BackgroundTask[]>('/api/tasks');
      this._tasks = tasks.sort((a, b) =>
        new Date(b.createdDate).getTime() - new Date(a.createdDate).getTime()
      );
      this.errorCount = 0;
      this.updatePollingInterval();
      this.dispatchEvent(new CustomEvent('tasks', { detail: this._tasks }));
    } catch {
      this.errorCount++;
      const backoffMs = Math.min(1000 * Math.pow(2, this.errorCount), 5 * 60 * 1000);
      this.currentIntervalMs = backoffMs;
    } finally {
      this.pollInFlight = false;
    }
  }

  private updatePollingInterval(): void {
    const hasRunning = this._tasks.some(t => t.status === 'Running');
    this.currentIntervalMs = hasRunning ? this.activeIntervalMs : this.idleIntervalMs;
  }

  private schedulePoll(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
    }
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      if (this.isVisible) {
        this.poll().finally(() => this.schedulePoll());
      }
    }, this.currentIntervalMs);
  }

  private handleVisibilityChange(): void {
    this.isVisible = !document.hidden;
    if (this.isVisible) {
      this.poll().finally(() => {
        if (this.pollTimer === null && this.isVisible) {
          this.schedulePoll();
        }
      });
    }
  }
}
