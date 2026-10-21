import { inject } from '@angular/core';
import { Router, CanActivateFn } from '@angular/router';
import { AUTH_CLIENT, TenantService } from '@PS/app-client-angular';
import { describeFailure } from '../shared/error-messages';

/**
 * The portal is for administrators only. The management app runs in Single tenancy mode, so the
 * shared TenantService has already selected its one tenant; being an administrator is having the
 * `admin` role there. (The registry is the only source of roles; token roles are ignored.)
 */
export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AUTH_CLIENT, { optional: true });
  const tenants = inject(TenantService);
  const router = inject(Router);

  if (!auth || !tenants.enabled) {
    return router.createUrlTree(['/no-access']);
  }

  // A failed sign-in is shown, not retried: retrying would loop through the identity provider.
  if (auth.getError()) {
    return router.createUrlTree(['/no-access']);
  }

  if (!auth.getAccount()) {
    void auth.signIn();
    return false;
  }

  const state = await tenants.ready();
  switch (state.status) {
    case 'selected':
      return tenants.hasRole('admin') ? true : router.createUrlTree(['/no-access']);
    case 'error':
      // Not "no access": the lookup itself failed, so say so instead of blaming the user.
      console.error('Admin guard error:', state.error);
      return router.createUrlTree(['/error'], { queryParams: { message: describeFailure(state.error) } });
    default:
      // not registered, or (not expected in Single mode) still choosing
      return router.createUrlTree(['/no-access']);
  }
};
