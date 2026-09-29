# @PS/app-client-angular

Angular 19+ adapter for the PS App Platform. Provides DI configuration, signals-based task service, HTTP interceptor, and unstyled task progress component.

## Installation

```bash
npm install @PS/app-client @PS/app-client-angular
```

## Usage

In your Angular app's bootstrap or component providers:

```typescript
import { provideAppPlatform } from '@PS/app-client-angular';

bootstrapApplication(AppComponent, {
  providers: [
    provideAppPlatform(),
    provideHttpClient(withInterceptors([platformAuthInterceptor])),
  ],
});
```

## Services

### BackgroundTaskService

```typescript
export class MyComponent {
  private tasks = inject(BackgroundTaskService);

  get isWorking(): boolean {
    return this.tasks.hasRunningTasks();
  }

  async createTask(): Promise<void> {
    const taskId = await this.tasks.createTask('MyTask', { data: 'test' });
  }

  ngOnInit(): void {
    this.tasks.tasks$.subscribe(tasks => {
      console.log('Tasks updated:', tasks);
    });
  }
}
```

### ConfigService

```typescript
const config = inject(ConfigService);
const apiRoot = config.get('api.root');
```

### Injection Tokens

- `APP_CONFIG` — the loaded AppConfig
- `API_CLIENT` — the ApiClient instance
- `AUTH_CLIENT` — the AuthClient instance (or null if auth not configured)

## Components

### TaskProgressComponent

An unstyled component showing running tasks with their progress percentages:

```html
<PS-task-progress></PS-task-progress>
```

Style the following CSS classes:
- `.ps-task-progress` — container
- `.ps-task-item` — individual task
- `.ps-task-header` — title area
- `.ps-task-type` — task type label
- `.ps-task-status` — status label
- `.ps-task-description` — task description
- `.ps-task-progress-bar` — progress bar container
- `.ps-task-progress-fill` — filled progress bar
- `.ps-task-percentage` — percentage text
