import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AUTH_CLIENT } from '@patrickschop/app-client-angular';

/**
 * Protects a route: a signed-out user is sent to the identity provider and brought back here.
 * When the app has no authentication configured there is nothing to sign in to, so it lets
 * everyone through.
 */
export const authGuard: CanActivateFn = () => {
  const auth = inject(AUTH_CLIENT, { optional: true });
  const router = inject(Router);

  if (!auth) {
    return true;
  }

  // A failed sign-in is shown, not retried: retrying would loop through the identity provider.
  if (auth.getError()) {
    return router.createUrlTree(['/forbidden'], { queryParams: { reason: 'error' } });
  }

  if (!auth.getAccount()) {
    void auth.signIn();
    return false;
  }

  return true;
};
