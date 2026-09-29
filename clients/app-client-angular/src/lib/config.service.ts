import { Injectable, inject } from '@angular/core';
import type { AppConfig } from '@PS/app-client';
import { APP_CONFIG } from './injection-tokens';

@Injectable({ providedIn: 'root' })
export class ConfigService {
  private appConfig = inject(APP_CONFIG);

  get config(): AppConfig {
    return this.appConfig;
  }

  get<T>(path: string, fallback?: T): T | undefined {
    const parts = path.split('.');
    let current: any = this.appConfig;

    for (const part of parts) {
      if (current?.[part] !== undefined) {
        current = current[part];
      } else {
        return fallback;
      }
    }

    return current as T;
  }
}
