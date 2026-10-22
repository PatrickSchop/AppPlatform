import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AUTH_CLIENT, ConfigService, TenantService } from '@PS/app-client-angular';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [RouterLink],
  template: `
    <h2>{{ title }}</h2>
    <p class="intro">
      A starter for apps on the PS App Platform. This page shows what the platform client found at
      startup; <a routerLink="/example">the example page</a> shows how to call the API.
    </p>

    <table class="details">
      <tbody>
        <tr>
          <th>API root</th>
          <td>{{ apiRoot }}</td>
        </tr>
        <tr>
          <th>Authentication</th>
          <td>
            @if (!auth) {
              Not configured
            } @else if (account) {
              Signed in as <strong>{{ account.name || account.username }}</strong>
            } @else {
              Not signed in
            }
          </td>
        </tr>
        <tr>
          <th>Tenancy</th>
          <td>
            @if (!tenants.enabled) {
              Off
            } @else if (tenants.current(); as tenant) {
              {{ tenant.name }}
              @if (tenant.roles.length) {
                <span class="badge">{{ tenant.roles.join(', ') }}</span>
              }
            } @else {
              No tenant selected
            }
          </td>
        </tr>
      </tbody>
    </table>

    @if (auth && !account) {
      <button class="btn-primary" (click)="auth.signIn()">Sign in</button>
    }
  `,
  styles: [`
    .intro {
      color: var(--color-text-muted);
    }
    .details {
      margin: 1.5rem 0;
    }
    .details th {
      width: 10rem;
      color: var(--color-text-muted);
    }
  `]
})
export class HomeComponent {
  protected auth = inject(AUTH_CLIENT, { optional: true });
  protected tenants = inject(TenantService);
  private config = inject(ConfigService);

  title = this.config.get<string>('title', 'App');
  apiRoot = this.config.get<string>('api.root', '(same origin)');
  account = this.auth?.getAccount() ?? null;
}
