// @ts-nocheck
import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { API_CLIENT, AUTH_CLIENT } from '@PS/app-client-angular';
import type { ApiClient, AuthClient } from '@PS/app-client';
import { MyTenantsResponse } from '../services/models';

export const adminGuard: CanActivateFn = async (route, state) => {
  // @ts-ignore
  const auth = inject(AUTH_CLIENT);
  // @ts-ignore
  const api = inject(API_CLIENT);
  const router = inject(Router);

  // Check if auth is available and user is signed in
  if (!auth) {
    return router.createUrlTree(['/no-access']);
  }

  // A failed sign-in is shown, not retried: retrying would loop through the identity provider.
  if (auth.getError()) {
    return router.createUrlTree(['/no-access']);
  }

  const account = auth.getAccount();
  if (!account) {
    auth.signIn();
    return false;
  }

  try {
    // Call GET api/me/tenants
    const response = await api.get<MyTenantsResponse>('me/tenants');

    // Check if user is registered and has admin role in any tenant
    if (response.registered && response.tenants.some((t: { roles: string[] }) => t.roles.includes('admin'))) {
      return true;
    }

    // Redirect to no-access
    return router.createUrlTree(['/no-access']);
  } catch (error: any) {
    // Not "no access": the lookup itself failed, so say so instead of blaming the user.
    console.error('Admin guard error:', error);
    const detail = typeof error?.body?.error === 'string' ? ` (${error.body.error})` : '';
    const message = error?.status
      ? `The management API returned HTTP ${error.status}${detail}.`
      : error?.message || 'The management API could not be reached.';
    return router.createUrlTree(['/error'], { queryParams: { message } });
  }
};
