export { loadConfig, resetConfigCache, type AppConfig } from './config.js';
export { ApiClient, ApiError, type ApiClientOptions } from './api-client.js';
export { TaskPoller, type BackgroundTask, type TaskPollerOptions } from './task-poller.js';
export { AuthClient, type AuthClientOptions, type AccountInfo } from './auth-client.js';

import { loadConfig, type AppConfig } from './config.js';
import { ApiClient } from './api-client.js';
import { TaskPoller } from './task-poller.js';
import { AuthClient } from './auth-client.js';

/**
 * Loads config, builds an AuthClient when auth is configured, and returns a wired ApiClient and TaskPoller.
 */
export async function createPlatformClient(options?: { configUrl?: string }): Promise<{
  config: AppConfig;
  auth: AuthClient | null;
  api: ApiClient;
  tasks: TaskPoller;
}> {
  const config = await loadConfig(options?.configUrl);

  let auth: AuthClient | null = null;
  if (config.auth?.tenantId && config.auth?.clientId && config.auth?.scopes) {
    auth = await AuthClient.create({
      tenantId: config.auth.tenantId,
      clientId: config.auth.clientId,
      scopes: config.auth.scopes,
    });
  }

  const baseUrl = config.api?.root || '';
  const api = new ApiClient({
    baseUrl,
    getAccessToken: auth ? () => auth.getAccessToken() : undefined,
    onUnauthorized: () => {
      if (auth) {
        auth.signIn().catch(() => {});
      }
    },
  });

  const tasks = new TaskPoller({ api });

  return { config, auth, api, tasks };
}
