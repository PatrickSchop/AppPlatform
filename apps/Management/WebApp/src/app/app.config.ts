import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAppPlatform, provideTenancy, platformAuthInterceptor } from '@patrickschop/app-client-angular';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withInterceptors([platformAuthInterceptor as any])),
    provideAppPlatform(),
    provideTenancy(),
  ],
};
