import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url';
import { Auth } from '../services/auth';
import { authInterceptor } from './auth-interceptor';

function base64url(json: unknown): string {
  return btoa(JSON.stringify(json)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

// A syntactically valid JWT (header.payload.signature) carrying whatever
// payload claims we need. The interceptor/Auth service never verify the
// signature client-side (only the real backend does), so any placeholder
// signature segment is fine for decodeJwtPayload to parse the payload.
function fakeJwt(payload: Record<string, unknown>): string {
  return `${base64url({ alg: 'HS256', typ: 'JWT' })}.${base64url(payload)}.signature`;
}

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: Auth;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(Auth);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('adds "Authorization: Bearer <token>" when the user is authenticated', () => {
    const validToken = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 3600 });
    auth.accessToken.set(validToken);
    expect(auth.isAuthenticated()).toBe(true);

    http.get(`${API_BASE_URL}/api/quotes?page=1&size=10`).subscribe();

    const req = httpMock.expectOne(`${API_BASE_URL}/api/quotes?page=1&size=10`);
    expect(req.request.headers.get('Authorization')).toBe(`Bearer ${validToken}`);
    req.flush([]);
  });

  it('does NOT add an Authorization header when there is no token (anonymous request)', () => {
    expect(auth.accessToken()).toBeNull();

    http.get(`${API_BASE_URL}/api/quotes?page=1&size=10`).subscribe();

    const req = httpMock.expectOne(`${API_BASE_URL}/api/quotes?page=1&size=10`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });

  it('does NOT add an Authorization header for requests to a different origin than the API base URL', () => {
    const validToken = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 3600 });
    auth.accessToken.set(validToken);

    http.get('https://not-the-quotes-api.example.com/ping').subscribe();

    const req = httpMock.expectOne('https://not-the-quotes-api.example.com/ping');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({});
  });

  // Real bug found during review: the interceptor originally attached
  // whatever string sat in `auth.accessToken()`, without checking whether
  // that token had already expired. Auth.isAuthenticated() (auth.ts) exists
  // specifically to make that distinction elsewhere in the app; the
  // interceptor was not using it, so a stale, expired token in localStorage
  // (e.g. left over from a session that ended over an hour ago) was sent to
  // the API as if the user were still logged in.
  it('does NOT add an Authorization header when the stored token has already expired', () => {
    const expiredToken = fakeJwt({ exp: Math.floor(Date.now() / 1000) - 60 });
    auth.accessToken.set(expiredToken);
    expect(auth.isAuthenticated()).toBe(false);

    http.get(`${API_BASE_URL}/api/quotes?page=1&size=10`).subscribe();

    const req = httpMock.expectOne(`${API_BASE_URL}/api/quotes?page=1&size=10`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush([]);
  });
});
