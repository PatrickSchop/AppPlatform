import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import type { AuthClient } from '@PS/app-client';
import { from } from 'rxjs';
import { mergeMap } from 'rxjs/operators';
import { AUTH_CLIENT, API_CLIENT, APP_CONFIG } from './injection-tokens';

export const platformAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const authClient = inject(AUTH_CLIENT);
  const config = inject(APP_CONFIG);

  if (!authClient) {
    return next(req);
  }

  const apiRoot = config.api?.root || '';
  const reqUrl = req.url;

  const isConfigRequest = reqUrl.includes('/configuration.json');
  const isCrossOrigin = !reqUrl.startsWith(apiRoot) && reqUrl.startsWith('http');
  const isApiRequest = apiRoot && reqUrl.startsWith(apiRoot);

  if (isConfigRequest || isCrossOrigin || !isApiRequest) {
    return next(req);
  }

  return from(authClient.getAccessToken()).pipe(
    mergeMap((token) => {
      if (token) {
        req = req.clone({
          setHeaders: { Authorization: `Bearer ${token}` },
        });
      }
      return next(req);
    })
  );
};
