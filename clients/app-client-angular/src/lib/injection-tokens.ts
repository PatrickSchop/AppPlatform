import { DOCUMENT } from '@angular/common';
import { InjectionToken, inject } from '@angular/core';
import type { AppConfig, ApiClient, AuthClient, TenantSession } from '@patrickschop/app-client';

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');
export const API_CLIENT = new InjectionToken<ApiClient>('API_CLIENT');
export const AUTH_CLIENT = new InjectionToken<AuthClient | null>('AUTH_CLIENT');

/** The tenant session, or null when the app does not run in Single or Multi tenancy mode. */
export const TENANT_SESSION = new InjectionToken<TenantSession | null>('TENANT_SESSION');

/**
 * What to do after the user switches tenant. The default reloads the page, so nothing the app
 * showed for the previous tenant stays on screen. Provide your own to reload only the data.
 */
export const TENANT_SWITCHED = new InjectionToken<() => void>('TENANT_SWITCHED', {
  providedIn: 'root',
  factory: () => {
    const document = inject(DOCUMENT);
    return () => document.defaultView?.location.reload();
  },
});
