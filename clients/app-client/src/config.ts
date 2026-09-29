export interface AppConfig {
  api?: { root?: string };
  auth?: { tenantId?: string; clientId?: string; scopes?: string[] };
  [key: string]: unknown;
}

let configCache: Promise<AppConfig> | null = null;

/**
 * Fetches /configuration.json. Caches the promise so concurrent callers share one request.
 */
export function loadConfig(url = '/configuration.json'): Promise<AppConfig> {
  if (!configCache) {
    configCache = fetch(url).then(res => {
      if (!res.ok) throw new Error(`Failed to load config: ${res.status}`);
      return res.json();
    });
  }
  return configCache;
}

/** Clears the cache. For tests and for a config reload after sign-in. */
export function resetConfigCache(): void {
  configCache = null;
}
