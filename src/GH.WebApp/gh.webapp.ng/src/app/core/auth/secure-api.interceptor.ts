import { HttpHandlerFn, HttpRequest } from '@angular/common/http';
import { getCookie } from './get-cookie';

/** Gateway paths that need the session cookie and, for writes, the XSRF header. */
const securePrefixes = ['/gh-api/', '/api/'];

/** Cookie the Gateway's XsrfMiddleware writes the antiforgery request token to. */
export const xsrfCookieName = 'XSRF-TOKEN';
export const xsrfHeaderName = 'X-XSRF-TOKEN';

export function secureApiInterceptor(request: HttpRequest<unknown>, next: HttpHandlerFn) {
  const url = new URL(request.url, globalThis.location.origin);
  const isGatewayCall =
    url.origin === globalThis.location.origin && securePrefixes.some((p) => url.pathname.startsWith(p));

  // Leave other origins alone, e.g. presigned uploads straight to object storage.
  if (!isGatewayCall) {
    return next(request);
  }

  return next(
    request.clone({
      withCredentials: true,
      setHeaders: { [xsrfHeaderName]: getCookie(xsrfCookieName) },
    }),
  );
}
