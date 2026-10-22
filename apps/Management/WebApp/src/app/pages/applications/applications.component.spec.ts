import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ApplicationListComponent } from './applications.component';
import { AdminService } from '../../services/admin.service';
import { API_CLIENT } from '@patrickschop/app-client-angular';
import type { ApplicationSummary } from '../../services/models';

describe('ApplicationListComponent', () => {
  let fixture: ComponentFixture<ApplicationListComponent>;
  let adminService: AdminService;

  const management: ApplicationSummary = {
    id: '1106d6f9-d832-4a71-8da2-d3ee8d3401f2',
    key: 'management',
    displayName: 'Application management',
    tenancy: 'Single',
    tenantCount: 1,
    userCount: 3,
    isDisabled: false
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ApplicationListComponent],
      providers: [{ provide: API_CLIENT, useValue: { get: vi.fn() } }]
    }).compileComponents();

    fixture = TestBed.createComponent(ApplicationListComponent);
    adminService = TestBed.inject(AdminService);
  });

  it('shows the name first, the key as a quiet secondary line, and the real tenancy and counts', async () => {
    vi.spyOn(adminService, 'getApplications').mockResolvedValue([management]);

    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();

    const row: HTMLElement = fixture.nativeElement.querySelector('tbody tr');
    const cells = Array.from(row.querySelectorAll('td')).map(td => td.textContent!.replace(/\s+/g, ' ').trim());
    expect(cells[0]).toBe('Application management management');
    expect(row.querySelector('.key')!.textContent).toBe('management');
    expect(cells.slice(1)).toEqual(['Single', '1', '3', 'Enabled']);
    expect(row.textContent).not.toContain(management.id);
  });

  it('marks a disabled application as disabled', async () => {
    vi.spyOn(adminService, 'getApplications').mockResolvedValue([{ ...management, isDisabled: true }]);

    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('tbody tr td:last-child').textContent).toContain('Disabled');
  });

  it('opens the application when its row is clicked', async () => {
    vi.spyOn(adminService, 'getApplications').mockResolvedValue([management]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();
    fixture.nativeElement.querySelector('tbody tr').click();

    expect(navigate).toHaveBeenCalledWith(['/applications', management.id]);
  });
});
