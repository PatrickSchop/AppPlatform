import { Component, inject } from '@angular/core';
import { AUTH_CLIENT } from '@patrickschop/app-client-angular';

/**
 * Three different problems with three different fixes:
 *  - not signed in: the user can fix it, so offer to sign in
 *  - signing in failed: show why, and offer another account
 *  - signed in but not allowed (the API said 403): the user cannot fix it; say who can
 */
@Component({
  selector: 'app-forbidden',
  standalone: true,
  template: `
    <div class="notice">
      @if (authError) {
        <h2>Sign-in failed</h2>
        <p class="detail">{{ authError }}</p>
        <button class="btn-primary" (click)="switchAccount()">Sign in with a different account</button>
      } @else if (!account) {
        <h2>You are not signed in</h2>
        <p>Sign in to use this app.</p>
        <button class="btn-primary" (click)="signIn()">Sign in</button>
      } @else {
        <h2>You do not have access</h2>
        <p>
          You are signed in as <strong>{{ account.name || account.username }}</strong>, but this account
          is not allowed to do that. Ask the administrator of this app to give you access; nothing you
          can change here will fix it.
        </p>
        <button class="btn-secondary" (click)="switchAccount()">Sign in with a different account</button>
      }
    </div>
  `,
  styles: [`
    .notice {
      max-width: 32rem;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-left: 4px solid var(--color-primary);
      border-radius: 0.375rem;
      padding: 1.5rem;
    }
    h2 {
      margin-top: 0;
    }
    .detail {
      font-family: monospace;
      font-size: 0.85rem;
      overflow-wrap: anywhere;
    }
  `]
})
export class ForbiddenComponent {
  private auth = inject(AUTH_CLIENT, { optional: true });

  account = this.auth?.getAccount() ?? null;
  authError = this.auth?.getError() ?? null;

  signIn() {
    void this.auth?.signIn();
  }

  switchAccount() {
    void this.auth?.switchAccount();
  }
}
