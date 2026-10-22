import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { describe, it, expect, vi } from 'vitest';
import { AUTH_CLIENT } from '@PS/app-client-angular';
import { authGuard } from './auth.guard';

function run(auth: unknown) {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: AUTH_CLIENT, useValue: auth }]
  });
  return TestBed.runInInjectionContext(() => authGuard({} as any, {} as any));
}

describe('authGuard', () => {
  it('lets everyone through when authentication is not configured', () => {
    expect(run(null)).toBe(true);
  });

  it('lets a signed-in user through', () => {
    expect(run({ getAccount: () => ({ username: 'p' }), getError: () => null, signIn: vi.fn() })).toBe(true);
  });

  it('sends a signed-out user to the identity provider and blocks the route', () => {
    const signIn = vi.fn();

    expect(run({ getAccount: () => null, getError: () => null, signIn })).toBe(false);
    expect(signIn).toHaveBeenCalled();
  });

  it('shows a failed sign-in instead of retrying it', () => {
    const signIn = vi.fn();
    const result = run({ getAccount: () => null, getError: () => 'AADSTS500011', signIn }) as UrlTree;

    expect(TestBed.inject(Router).serializeUrl(result)).toBe('/forbidden?reason=error');
    expect(signIn).not.toHaveBeenCalled();
  });
});
