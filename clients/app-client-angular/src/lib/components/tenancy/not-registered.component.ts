import { Component, inject } from '@angular/core';
import { AUTH_CLIENT } from '../../injection-tokens';

/** Shown to a signed-in user who is not registered anywhere: who they are, what to do, and a way out. */
@Component({
  selector: 'ps-not-registered',
  standalone: true,
  template: `
    <section class="ps-not-registered">
      <h1 class="ps-not-registered-title">You do not have access</h1>
      @if (accountName) {
        <p class="ps-not-registered-account">Signed in as <strong>{{ accountName }}</strong></p>
      }
      <p class="ps-not-registered-help">Ask an administrator to invite you.</p>
      <button type="button" class="ps-not-registered-signout" (click)="signOut()">Sign out</button>
    </section>
  `,
})
export class NotRegisteredComponent {
  private readonly auth = inject(AUTH_CLIENT, { optional: true }) ?? null;

  protected get accountName(): string | null {
    const account = this.auth?.getAccount();
    return account?.name || account?.username || null;
  }

  signOut(): void {
    void this.auth?.signOut();
  }
}
