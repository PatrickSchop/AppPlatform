import { Component, inject } from '@angular/core';
import { TenantService } from '../../tenant.service';
import { TENANT_SWITCHED } from '../../injection-tokens';

/** A drop-down to change tenant, for the app's header. Renders nothing unless there is more than one. */
@Component({
  selector: 'ps-tenant-switcher',
  standalone: true,
  template: `
    @if (tenants.tenants().length > 1) {
      <label class="ps-tenant-switcher">
        <span class="ps-tenant-switcher-label">Tenant</span>
        <select class="ps-tenant-switcher-select" (change)="choose($any($event.target).value)">
          @for (tenant of tenants.tenants(); track tenant.tenantId) {
            <option [value]="tenant.tenantId" [selected]="tenant.tenantId === tenants.current()?.tenantId">
              {{ tenant.name }}
            </option>
          }
        </select>
      </label>
    }
  `,
})
export class TenantSwitcherComponent {
  protected readonly tenants = inject(TenantService);
  private readonly switched = inject(TENANT_SWITCHED);

  choose(tenantId: string): void {
    if (tenantId === this.tenants.current()?.tenantId) return;
    this.tenants.select(tenantId);
    this.switched();
  }
}
