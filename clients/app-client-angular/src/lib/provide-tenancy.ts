import { APP_INITIALIZER, EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { whenPlatformClientReady } from './provide-app-platform';

/**
 * Makes the app wait, before its first render, for the tenant lookup that started at sign-in.
 * Without it the first screen is drawn with no tenant and flickers into place. A no-op when the
 * app does not run in Single or Multi tenancy mode.
 *
 * Use it next to provideAppPlatform(); the order of the two does not matter.
 */
export function provideTenancy(): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: APP_INITIALIZER,
      multi: true,
      useFactory: () => async () => {
        const client = await whenPlatformClientReady();
        await client.tenant?.ready();
      },
    },
  ]);
}
