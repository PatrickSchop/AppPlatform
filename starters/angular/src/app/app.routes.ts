import { Routes } from '@angular/router';
import { NotRegisteredComponent, TenantPickerComponent, tenantGuard } from '@patrickschop/app-client-angular';
import { authGuard } from './guards/auth.guard';
import { HomeComponent } from './pages/home/home.component';
import { ExampleComponent } from './pages/example/example.component';
import { ForbiddenComponent } from './pages/forbidden/forbidden.component';
import { ErrorComponent } from './pages/error/error.component';

export const routes: Routes = [
  // Public: these explain the state the user is in, so they cannot require it.
  { path: '', component: HomeComponent, pathMatch: 'full' },
  { path: 'forbidden', component: ForbiddenComponent },
  { path: 'error', component: ErrorComponent },
  { path: 'not-registered', component: NotRegisteredComponent },
  { path: 'select-tenant', component: TenantPickerComponent, canActivate: [authGuard] },
  {
    // Authentication first, then the tenant. Both are pass-throughs when the app has neither.
    path: '',
    canActivate: [authGuard, tenantGuard],
    children: [{ path: 'example', component: ExampleComponent }],
  },
  { path: '**', redirectTo: '' },
];
