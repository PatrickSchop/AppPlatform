import { useSyncExternalStore, useCallback, useEffect, useState } from 'react';
import type { AppConfig, ApiClient, AuthClient, BackgroundTask } from '@PS/app-client';
import { usePlatformContext } from './context';

export function usePlatform() {
  return usePlatformContext();
}

export function useConfig(): AppConfig {
  const platform = usePlatformContext();
  return platform.config;
}

export function useApi(): ApiClient {
  const platform = usePlatformContext();
  return platform.api;
}

export function useAuth(): AuthClient | null {
  const platform = usePlatformContext();
  return platform.auth;
}

export function useBackgroundTasks() {
  const platform = usePlatformContext();
  const poller = platform.tasks;

  const subscribe = useCallback(
    (listener: (tasks: readonly BackgroundTask[]) => void) => {
      return poller.subscribe(listener);
    },
    [poller]
  );

  const getSnapshot = useCallback(() => poller.tasks, [poller]);

  const tasks = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);

  return {
    tasks,
    runningTasks: tasks.filter((t: BackgroundTask) => t.status === 'Running'),
    hasRunningTasks: tasks.some((t: BackgroundTask) => t.status === 'Running'),
    refresh: () => poller.refresh(),
    createTask: (
      taskType: string,
      taskData: unknown,
      description?: string,
      requiresNotification?: boolean
    ) => poller.createTask(taskType, taskData, description, requiresNotification),
  };
}

export interface UseApiQueryOptions {
  enabled?: boolean;
}

export interface UseApiQueryResult<T> {
  data: T | undefined;
  error: Error | undefined;
  loading: boolean;
  refetch: () => Promise<void>;
}

export function useApiQuery<T = unknown>(
  path: string | null,
  options?: UseApiQueryOptions
): UseApiQueryResult<T> {
  const api = useApi();
  const [data, setData] = useState<T | undefined>();
  const [error, setError] = useState<Error | undefined>();
  const [loading, setLoading] = useState(false);

  const enabled = options?.enabled !== false;

  const refetch = useCallback(async () => {
    if (!path || !enabled) return;

    const controller = new AbortController();
    setLoading(true);
    setError(undefined);

    try {
      const result = await api.get<T>(path, { signal: controller.signal });
      setData(result);
    } catch (err) {
      if (err instanceof Error && err.name !== 'AbortError') {
        setError(err);
      }
    } finally {
      setLoading(false);
    }
  }, [api, path, enabled]);

  useEffect(() => {
    if (!path || !enabled) {
      setData(undefined);
      return;
    }

    const controller = new AbortController();
    setLoading(true);

    (async () => {
      try {
        const result = await api.get<T>(path, { signal: controller.signal });
        if (!controller.signal.aborted) {
          setData(result);
          setError(undefined);
        }
      } catch (err) {
        if (!controller.signal.aborted) {
          setError(err instanceof Error ? err : new Error(String(err)));
        }
      } finally {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      }
    })();

    return () => controller.abort();
  }, [api, path, enabled]);

  return { data, error, loading, refetch };
}
