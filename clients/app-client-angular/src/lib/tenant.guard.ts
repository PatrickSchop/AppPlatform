import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TenantService } from './tenant.service';

/**
 * Lets a signed-in user through once a tenant is selected. Put it after the authentication guard.
 *
 * - tenancy off, or a tenant selected: allowed
 * - several tenants and none chosen: /select-tenant, remembering where the user was going
 * - not registered: /not-registered
 * - the lookup failed: the error is rethrown so the app's error handling shows it
 *
 * The routes are the ones the starters use; an app that keeps them elsewhere writes its own guard
 * on top of TenantService.
 */
export const tenantGuard: CanActivateFn = async (_route, state) => {
  const tenants = inject(TenantService);
  const router = inject(Router);

  if (!tenants.enabled) return true;

  const tenantState = await tenants.ready();
  switch (tenantState.status) {
    case 'selected':
      return true;
    case 'selecting':
      return router.createUrlTree(['/select-tenant'], { queryParams: { returnUrl: state.url } });
    case 'not-registered':
      return router.createUrlTree(['/not-registered']);
    case 'error':
      throw tenantState.error;
    default:
      return router.createUrlTree(['/not-registered']);
  }
};
