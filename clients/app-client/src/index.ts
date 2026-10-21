export { loadConfig, resetConfigCache, type AppConfig } from './config.js';
export { ApiClient, ApiError, type ApiClientOptions } from './api-client.js';
export { TaskPoller, type BackgroundTask, type TaskPollerOptions } from './task-poller.js';
export { AuthClient, type AuthClientOptions, type AccountInfo } from './auth-client.js';
export {
  TenantSession,
  type TenantInfo,
  type TenantState,
  type TenantSessionOptions,
  type MyTenantsResponse,
} from './tenant-session.js';

import { loadConfig, type AppConfig } from './config.js';
import { ApiClient } from './api-client.js';
import { TaskPoller } from './task-poller.js';
import { AuthClient } from './auth-client.js';
import { TenantSession } from './tenant-session.js';

/**
 * Loads config, builds an AuthClient when auth is configured, and returns a wired ApiClient and
 * TaskPoller. In Single and Multi tenancy mode it also builds a TenantSession: the selected tenant
 * is sent on every request, and polling waits until a tenant is selected.
 */
export async function createPlatformClient(options?: { configUrl?: string }): Promise<{
  config: AppConfig;
  auth: AuthClient | null;
  api: ApiClient;
  tasks: TaskPoller;
  tenant: TenantSession | null;
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

  const tenancy = config.tenancy && config.tenancy.mode !== 'None' ? config.tenancy : null;

  // The session is created after the ApiClient (it needs it), but the ApiClient's hooks must reach it.
  let tenant: TenantSession | null = null;

  const baseUrl = config.api?.root || '';
  const api = new ApiClient({
    baseUrl,
    getAccessToken: auth ? () => auth.getAccessToken() : undefined,
    onUnauthorized: () => {
      if (auth) {
        auth.signIn().catch(() => {});
      }
    },
    getHeaders: () => tenant?.headers() ?? {},
    onErrorCode: (_status, code) => {
      if (code === 'tenant_required' || code === 'tenant_forbidden') {
        tenant?.reset();
      } else if (code === 'not_registered') {
        void tenant?.load();
      }
    },
  });

  // Polling waits for a selected tenant, or it would poll into 409s.
  const tasks = new TaskPoller({ api, autoStart: !tenancy });

  if (tenancy) {
    const account = auth?.getAccount();
    tenant = new TenantSession({
      api,
      header: tenancy.header,
      // Per app and per account, so switching accounts never reuses another account's tenant.
      storageKey: `ps-tenant:${location.origin}:${account?.homeAccountId ?? 'anon'}`,
    });

    // Task lists are per tenant: follow the selection.
    tenant.addEventListener('change', () => {
      if (tenant!.state.status === 'selected') {
        tasks.start();
        void tasks.refresh();
      } else {
        tasks.stop();
      }
    });

    // Without a signed-in user the request would only bounce to sign-in, which the app drives.
    if (!auth || account) {
      void tenant.load();
    }
  }

  return { config, auth, api, tasks, tenant };
}
