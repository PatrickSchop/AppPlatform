import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TenantService } from '../../tenant.service';
import { safeReturnUrl } from './safe-return-url';

/** Lists the user's tenants; choosing one remembers it and continues to `returnUrl`. */
@Component({
  selector: 'ps-tenant-picker',
  standalone: true,
  template: `
    <section class="ps-tenant-picker">
      <h1 class="ps-tenant-picker-title">Choose a tenant</h1>
      <ul class="ps-tenant-list">
        @for (tenant of tenants.tenants(); track tenant.tenantId) {
          <li>
            <button type="button" class="ps-tenant-option" (click)="choose(tenant.tenantId)">
              <span class="ps-tenant-name">{{ tenant.name }}</span>
              @if (tenant.roles.length) {
                <span class="ps-tenant-roles">{{ tenant.roles.join(', ') }}</span>
              }
            </button>
          </li>
        }
      </ul>
    </section>
  `,
})
export class TenantPickerComponent implements OnInit {
  protected readonly tenants = inject(TenantService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  ngOnInit(): void {
    // Arriving here with nothing to choose (already selected, or not registered) must not strand the user.
    const state = this.tenants.state();
    if (state.status === 'selected') {
      void this.router.navigateByUrl(this.returnUrl());
    } else if (state.status === 'not-registered') {
      void this.router.navigateByUrl('/not-registered');
    }
  }

  choose(tenantId: string): void {
    this.tenants.select(tenantId);
    void this.router.navigateByUrl(this.returnUrl());
  }

  private returnUrl(): string {
    return safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
  }
}
