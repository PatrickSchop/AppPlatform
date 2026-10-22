import { Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AUTH_CLIENT, ConfigService, TenantSwitcherComponent } from '@patrickschop/app-client-angular';
import { ToastComponent } from './shared/toast.component';
import { TaskStatusComponent } from './components/task-status/task-status.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastComponent, TenantSwitcherComponent, TaskStatusComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  private auth = inject(AUTH_CLIENT, { optional: true });

  title = inject(ConfigService).get<string>('title', 'App');
  authEnabled = !!this.auth;
  accountName = signal(this.auth?.getAccount()?.name ?? null);

  signIn() {
    void this.auth?.signIn();
  }

  signOut() {
    void this.auth?.signOut();
  }
}
