import {
  ReactNode,
  useState,
  useEffect,
  useRef,
  JSX,
} from 'react';
import { createPlatformClient } from '@PS/app-client';
import { PlatformContext } from './context';

type PlatformClient = Awaited<ReturnType<typeof createPlatformClient>>;

export interface PlatformProviderProps {
  configUrl?: string;
  children: ReactNode;
  fallback?: ReactNode;
  errorFallback?: (error: Error, retry: () => void) => ReactNode;
}

export function PlatformProvider({
  configUrl,
  children,
  fallback = null,
  errorFallback,
}: PlatformProviderProps): JSX.Element {
  const [client, setClient] = useState<PlatformClient | null>(null);
  const [error, setError] = useState<Error | null>(null);
  const initRef = useRef(false);

  const initialize = async () => {
    try {
      const platformClient = await createPlatformClient({ configUrl });
      setClient(platformClient);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)));
    }
  };

  useEffect(() => {
    if (initRef.current) return;
    initRef.current = true;

    initialize();

    return () => {
      if (client) {
        client.tasks.stop();
      }
    };
  }, [client]);

  if (error && errorFallback) {
    return <>{errorFallback(error, initialize)}</>;
  }

  if (!client) {
    return <>{fallback}</>;
  }

  return (
    <PlatformContext.Provider value={client}>
      {children}
    </PlatformContext.Provider>
  );
}
