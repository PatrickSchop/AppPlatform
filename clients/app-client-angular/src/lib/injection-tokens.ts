import { InjectionToken } from '@angular/core';
import type { AppConfig, ApiClient, AuthClient } from '@PS/app-client';

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG');
export const API_CLIENT = new InjectionToken<ApiClient>('API_CLIENT');
export const AUTH_CLIENT = new InjectionToken<AuthClient | null>('AUTH_CLIENT');
