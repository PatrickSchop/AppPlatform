import { Routes } from '@angular/router';
import { adminGuard } from './guards/admin.guard';
import { NoAccessComponent } from './pages/no-access/no-access.component';
import { ApplicationListComponent } from './pages/applications/applications.component';
import { ApplicationDetailComponent } from './pages/applications/application-detail.component';
import { TenantDetailComponent } from './pages/tenants/tenant-detail.component';
import { TeamDetailComponent } from './pages/teams/team-detail.component';
import { AcceptInviteComponent } from './pages/accept-invite/accept-invite.component';
import { UserListComponent } from './pages/users/user-list.component';
import { UserDetailComponent } from './pages/users/user-detail.component';

export const routes: Routes = [
  { path: '', redirectTo: 'applications', pathMatch: 'full' },
  { path: 'no-access', component: NoAccessComponent },
  { path: 'invite/:token', component: AcceptInviteComponent },
  {
    path: '',
    canActivate: [adminGuard],
    children: [
      { path: 'applications', component: ApplicationListComponent },
      { path: 'applications/:appId', component: ApplicationDetailComponent },
      { path: 'applications/:appId/tenants/:tenantId', component: TenantDetailComponent },
      { path: 'applications/:appId/tenants/:tenantId/teams/:teamId', component: TeamDetailComponent },
      { path: 'users', component: UserListComponent },
      { path: 'users/:userId', component: UserDetailComponent }
    ]
  }
];
