import { ComponentFixture, TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { NoAccessComponent } from './no-access.component';
import { AUTH_CLIENT } from '@patrickschop/app-client-angular';

describe('NoAccessComponent', () => {
  let fixture: ComponentFixture<NoAccessComponent>;
  let auth: any;

  async function create(error: string | null) {
    auth = {
      getAccount: () => ({ username: 'q@example.com', name: 'Q' }),
      getError: () => error,
      getAccessToken: () => Promise.resolve(null),
      switchAccount: vi.fn(),
      signOut: vi.fn()
    };
    await TestBed.configureTestingModule({
      imports: [NoAccessComponent],
      providers: [{ provide: AUTH_CLIENT, useValue: auth }]
    }).compileComponents();
    fixture = TestBed.createComponent(NoAccessComponent);
    fixture.detectChanges();
  }

  const buttons = () => Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button'));

  it('offers to sign in with a different account', async () => {
    await create(null);

    const button = buttons().find(b => b.textContent!.includes('different account'))!;
    button.click();

    expect(auth.switchAccount).toHaveBeenCalled();
  });

  it('explains a missing organisation grant and still offers another account', async () => {
    await create('invalid_resource: AADSTS500011: The resource principal named api://x was not found');

    expect(fixture.nativeElement.textContent).toContain('organisation has not been given access');
    expect(buttons().some(b => b.textContent!.includes('different account'))).toBe(true);
  });

  it('does not describe an ordinary no-access as a sign-in failure', async () => {
    await create(null);

    expect(fixture.nativeElement.textContent).not.toContain('Sign-in failed');
  });
});
