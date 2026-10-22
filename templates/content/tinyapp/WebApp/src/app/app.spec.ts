import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, it, expect, vi } from 'vitest';
import { AppComponent } from './app';
import { APP_CONFIG, AUTH_CLIENT, BackgroundTaskService } from '@PS/app-client-angular';
import { signal } from '@angular/core';

function setup(auth: unknown) {
  TestBed.configureTestingModule({
    imports: [AppComponent],
    providers: [
      provideRouter([]),
      { provide: APP_CONFIG, useValue: { title: 'Test App' } },
      { provide: AUTH_CLIENT, useValue: auth },
      {
        provide: BackgroundTaskService,
        useValue: { tasks: signal([]), runningTasks: signal([]), hasRunningTasks: signal(false) }
      }
    ]
  });
  const fixture = TestBed.createComponent(AppComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('AppComponent', () => {
  it('shows the configured title and real navigation links', () => {
    const el = setup(null);

    expect(el.querySelector('h1')!.textContent).toBe('Test App');
    const hrefs = Array.from(el.querySelectorAll('.nav-link')).map(a => a.getAttribute('href'));
    expect(hrefs).toEqual(['/', '/example']);
  });

  it('offers sign-out to a signed-in user', () => {
    const signOut = vi.fn();
    const el = setup({ getAccount: () => ({ name: 'P' }), signOut });

    expect(el.querySelector('.account-name')!.textContent).toBe('P');
    (el.querySelector('.sign-out-btn') as HTMLElement).click();
    expect(signOut).toHaveBeenCalled();
  });

  it('offers sign-in to a signed-out user, and nothing when auth is not configured', () => {
    expect(setup({ getAccount: () => null }).querySelector('.sign-out-btn')!.textContent).toBe('Sign In');
    TestBed.resetTestingModule();
    expect(setup(null).querySelector('.sign-out-btn')).toBeNull();
  });
});
