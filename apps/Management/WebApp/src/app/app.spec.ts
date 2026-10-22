import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, it, expect, vi } from 'vitest';
import { AppComponent } from './app';
import { AUTH_CLIENT } from '@patrickschop/app-client-angular';

describe('AppComponent', () => {
  it('renders the sidebar entries as real links', async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideRouter([]),
        { provide: AUTH_CLIENT, useValue: { getAccount: () => ({ name: 'P' }), signOut: vi.fn() } }
      ]
    }).compileComponents();

    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    const hrefs = Array.from<HTMLAnchorElement>(fixture.nativeElement.querySelectorAll('.nav-link'))
      .map(a => a.getAttribute('href'));
    expect(hrefs).toEqual(['/applications', '/users']);
  });
});
