import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { LoginRequest, RegisterRequest, TokenResponse } from '../models/auth.models';
import { API_BASE_URL } from '../api-base-url';
import { AppError } from '../models/app-error.models';

const STORAGE_KEY = 'quotesapi.access_token';

// The self-issued JWT's claim URIs, exactly as emitted by JwtSecurityTokenHandler's
// default outbound claim mapping for ClaimTypes.NameIdentifier / ClaimTypes.Email
// (see Program.cs POST /api/auth/login). Confirmed by decoding a real access_token.
const CLAIM_USER_ID = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier';
const CLAIM_EMAIL = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';
const CLAIM_SCOPE = 'scope';

interface DecodedAccessToken {
  [CLAIM_USER_ID]?: string;
  [CLAIM_EMAIL]?: string;
  scope?: string;
  exp?: number;
}

function decodeJwtPayload(token: string): DecodedAccessToken | null {
  const segments = token.split('.');
  if (segments.length !== 3) {
    return null;
  }
  try {
    const base64 = segments[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(base64)) as DecodedAccessToken;
  } catch {
    return null;
  }
}

function readStoredToken(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

@Injectable({
  providedIn: 'root',
})
export class Auth {
  private readonly http = inject(HttpClient);

  readonly accessToken = signal<string | null>(readStoredToken());
  readonly loginPending = signal(false);
  readonly loginError = signal<string | null>(null);

  readonly registerPending = signal(false);
  readonly registerError = signal<string | null>(null);
  readonly registerSuccess = signal(false);

  private readonly decodedToken = computed(() => {
    const token = this.accessToken();
    return token ? decodeJwtPayload(token) : null;
  });

  // A stored token whose exp claim has already passed must not count as
  // authenticated: the API rejects it, but the old check (token !== null)
  // reported isAuthenticated() === true regardless of expiry, which left
  // the UI showing "Logged in" while every write silently 401'd.
  readonly isAuthenticated = computed(() => {
    const exp = this.decodedToken()?.exp;
    return exp !== undefined && exp * 1000 > Date.now();
  });

  readonly currentUserId = computed(() => {
    const raw = this.decodedToken()?.[CLAIM_USER_ID];
    return raw ? Number(raw) : null;
  });

  readonly currentUserEmail = computed(
    () => this.decodedToken()?.[CLAIM_EMAIL] ?? null,
  );

  readonly currentUserScope = computed(
    () => this.decodedToken()?.[CLAIM_SCOPE] ?? null,
  );

  constructor() {
    // Side effect: keep the browser's persisted token in sync with the signal,
    // so a page reload does not silently log the user out.
    effect(() => {
      const token = this.accessToken();
      try {
        if (token) {
          localStorage.setItem(STORAGE_KEY, token);
        } else {
          localStorage.removeItem(STORAGE_KEY);
        }
      } catch {
        // Storage can be unavailable (e.g. private browsing); ignore.
      }
    });
  }

  login(request: LoginRequest): void {
    this.loginPending.set(true);
    this.loginError.set(null);

    this.http.post<TokenResponse>(`${API_BASE_URL}/api/auth/login`, request).subscribe({
      next: (response) => {
        this.accessToken.set(response.access_token);
        this.loginPending.set(false);
      },
      error: (err: AppError) => {
        this.loginError.set(
          err.kind === 'unauthorized' ? 'Invalid email or password.' : 'Login failed. Please try again.',
        );
        this.loginPending.set(false);
      },
    });
  }

  // POST /api/auth/register (Program.cs) creates the account but does not log the user
  // in — it returns no token, unlike /api/auth/login. registerSuccess is what the
  // Register page watches to navigate to /login once the account actually exists.
  register(request: RegisterRequest): void {
    this.registerPending.set(true);
    this.registerError.set(null);
    this.registerSuccess.set(false);

    this.http.post(`${API_BASE_URL}/api/auth/register`, request).subscribe({
      next: () => {
        this.registerPending.set(false);
        this.registerSuccess.set(true);
      },
      error: (err: AppError) => {
        // Every AppError variant already carries a ready-to-render message (validation:
        // the specific field error; 409 conflict: the backend's "already exists" title;
        // network/server: a safe generic message) — no extra mapping needed here.
        this.registerError.set(err.message);
        this.registerPending.set(false);
      },
    });
  }

  logout(): void {
    this.accessToken.set(null);
  }
}
