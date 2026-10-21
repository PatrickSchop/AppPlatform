// @ts-nocheck
import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AUTH_CLIENT } from '@PS/app-client-angular';
import type { AuthClient } from '@PS/app-client';

@Component({
  selector: 'app-no-access',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './no-access.component.html',
  styleUrl: './no-access.component.scss'
})
export class NoAccessComponent implements OnInit {
  // @ts-ignore
  private auth = inject(AUTH_CLIENT);

  accountName: string | null = null;
  oid: string | null = null;
  tid: string | null = null;
  authError: string | null = null;

  ngOnInit() {
    if (this.auth) {
      const account = this.auth.getAccount();
      this.accountName = account?.name || null;
      this.authError = this.auth.getError();

      // Get OID and TID from JWT token
      this.auth.getAccessToken().then((token: string | null) => {
        if (token) {
          try {
            const payload = JSON.parse(atob(token.split('.')[1]));
            this.oid = payload.oid || null;
            this.tid = payload.tid || null;
          } catch (e) {
            console.error('Failed to decode token:', e);
          }
        }
      });
    }
  }

  signOut() {
    if (this.auth) {
      this.auth.signOut();
    }
  }

  copyToClipboard(text: string) {
    navigator.clipboard.writeText(text);
  }
}
