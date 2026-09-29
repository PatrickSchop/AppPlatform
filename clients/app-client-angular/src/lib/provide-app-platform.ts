import {
  EnvironmentProviders,
  makeEnvironmentProviders,
  APP_INITIALIZER,
  inject,
} from '@angular/core';
import { HTTP_INTERCEPTORS } from '@angular/common/http';
import { createPlatformClient } from '@PS/app-client';
import { APP_CONFIG, API_CLIENT, AUTH_CLIENT } from './injection-tokens';
import { platformAuthInterceptor } from './auth-interceptor';
import { BackgroundTaskService } from './background-task.service';

export interface AppPlatformConfig {
  configUrl?: string;
  required?: boolean;
}

let clientInstance: ReturnType<typeof createPlatformClient> | null = null;

const initializeClient = async (configUrl?: string) => {
  if (!clientInstance) {
    clientInstance = createPlatformClient({ configUrl });
  }
  return clientInstance;
};

export function provideAppPlatform(config?: AppPlatformConfig): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: APP_INITIALIZER,
      useFactory: () => {
        return () => initializeClient(config?.configUrl);
      },
      multi: true,
    },
    {
      provide: APP_CONFIG,
      useFactory: async () => {
        const client = await initializeClient(config?.configUrl);
        return client.config;
      },
    },
    {
      provide: API_CLIENT,
      useFactory: async () => {
        const client = await initializeClient(config?.configUrl);
        return client.api;
      },
    },
    {
      provide: AUTH_CLIENT,
      useFactory: async () => {
        const client = await initializeClient(config?.configUrl);
        return client.auth;
      },
    },
    {
      provide: 'TASK_POLLER',
      useFactory: async () => {
        const client = await initializeClient(config?.configUrl);
        return client.tasks;
      },
    },
    {
      provide: HTTP_INTERCEPTORS,
      useValue: platformAuthInterceptor,
      multi: true,
    },
    BackgroundTaskService,
  ]);
}
