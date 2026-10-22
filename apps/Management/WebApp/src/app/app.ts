import { Component, inject, OnInit } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AUTH_CLIENT } from '@patrickschop/app-client-angular';
import type { AuthClient } from '@patrickschop/app-client';
import { TenantSwitcherComponent } from '@patrickschop/app-client-angular';
import { ToastComponent } from './shared/toast.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommonModule, ToastComponent, TenantSwitcherComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  // @ts-ignore
  private auth = inject(AUTH_CLIENT);
  private router = inject(Router);

  accountName: string | null = null;
  sidenavOpen = true;

  ngOnInit() {
    if (this.auth) {
      const account = this.auth.getAccount();
      this.accountName = account?.name || null;
    }
  }

  signOut() {
    if (this.auth) {
      this.auth.signOut();
    }
  }
}
