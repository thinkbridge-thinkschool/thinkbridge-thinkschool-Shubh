import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, RouterStateSnapshot, UrlTree } from '@angular/router';
import { Auth } from '../services/auth';
import { authGuard } from './auth-guard';

const STORAGE_KEY = 'quotesapi.access_token';

// Not a verified signature (see Auth.decodeJwtPayload in auth.ts) — the app
// never validates the JWT client-side, only decodes the payload, so an
// unsigned token with the right claims is enough to exercise the guard.
function fakeToken(exp: number): string {
  const header = btoa(JSON.stringify({ alg: 'none', typ: 'JWT' }));
  const payload = btoa(JSON.stringify({ exp }));
  return `${header}.${payload}.`;
}

describe('authGuard', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  });

  function runGuard() {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot),
    );
  }

  it('redirects to /login when there is no authenticated session', () => {
    const result = runGuard();
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/login');
  });

  it('allows activation when the session token has not expired', () => {
    localStorage.setItem(STORAGE_KEY, fakeToken(Math.floor(Date.now() / 1000) + 3600));
    expect(TestBed.inject(Auth).isAuthenticated()).toBe(true);

    expect(runGuard()).toBe(true);
  });

  it('redirects to /login when the stored token has already expired', () => {
    localStorage.setItem(STORAGE_KEY, fakeToken(Math.floor(Date.now() / 1000) - 3600));
    expect(TestBed.inject(Auth).isAuthenticated()).toBe(false);

    const result = runGuard();
    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe('/login');
  });
});
