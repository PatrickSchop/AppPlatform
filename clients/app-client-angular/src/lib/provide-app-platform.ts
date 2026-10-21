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

type PlatformClient = Awaited<ReturnType<typeof createPlatformClient>>;

let clientPromise: Promise<PlatformClient> | null = null;
let client: PlatformClient | null = null;

// Resolved by APP_INITIALIZER before bootstrap completes, so the providers below can
// hand out the client itself. An async useFactory would inject a Promise instead.
const initializeClient = async (configUrl?: string) => {
  clientPromise ??= createPlatformClient({ configUrl });
  client = await clientPromise;
  return client;
};

const requireClient = (): PlatformClient => {
  if (!client) {
    throw new Error('Platform client is not initialized; provideAppPlatform() must run before it is injected.');
  }
  return client;
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
      useFactory: () => requireClient().config,
    },
    {
      provide: API_CLIENT,
      useFactory: () => requireClient().api,
    },
    {
      provide: AUTH_CLIENT,
      useFactory: () => requireClient().auth,
    },
    {
      provide: 'TASK_POLLER',
      useFactory: () => requireClient().tasks,
    },
    {
      provide: HTTP_INTERCEPTORS,
      useValue: platformAuthInterceptor,
      multi: true,
    },
    BackgroundTaskService,
  ]);
}
