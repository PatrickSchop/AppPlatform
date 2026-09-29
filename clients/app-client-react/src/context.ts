import { createContext, useContext } from 'react';
import { createPlatformClient } from '@PS/app-client';

type PlatformClient = Awaited<ReturnType<typeof createPlatformClient>>;

export const PlatformContext = createContext<PlatformClient | null>(null);

export function usePlatformContext(): PlatformClient {
  const context = useContext(PlatformContext);
  if (!context) {
    throw new Error(
      'usePlatform must be called from within a <PlatformProvider>'
    );
  }
  return context;
}
