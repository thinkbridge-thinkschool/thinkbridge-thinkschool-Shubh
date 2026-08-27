import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Auth } from '../services/auth';

// Functional guard (Angular's standalone-first API, no NgModule needed).
// Protects every quote route: an authenticated session (a non-expired token,
// per Auth.isAuthenticated — see auth.ts) may proceed, everything else is
// redirected to /login. Returning a UrlTree instead of `false` means the
// browser URL actually reflects where the user landed instead of silently
// blocking navigation.
export const authGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  const router = inject(Router);

  return auth.isAuthenticated() || router.createUrlTree(['/login']);
};
