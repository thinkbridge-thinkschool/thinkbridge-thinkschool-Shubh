import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Auth } from '../services/auth';
import { API_BASE_URL } from '../api-base-url';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(Auth);
  const token = auth.accessToken();

  // "the user is authenticated" must mean auth.isAuthenticated() (a token
  // that also hasn't expired), not merely "a token string exists". A token
  // left in localStorage from a session that already expired is still
  // non-null, so checking `!token` alone attached a Bearer header the real
  // backend would reject anyway, sending an authenticated-looking request
  // for a user who is actually logged out.
  if (!token || !auth.isAuthenticated() || !req.url.startsWith(API_BASE_URL)) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
