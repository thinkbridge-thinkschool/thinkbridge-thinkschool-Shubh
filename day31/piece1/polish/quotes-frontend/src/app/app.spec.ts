import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';

// App itself is just a shell hosting <router-outlet /> (see app.html) — it is
// never the routed component, so these tests drive real navigation through
// the harness and assert on whatever route actually activates.
describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
      ],
    }).compileComponents();
  });

  // No stored token -> Auth.isAuthenticated() is false -> authGuard redirects
  // the default '' route to /login, which renders Login (h1 "QuotesApi").
  it('redirects an unauthenticated user to the login screen', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/');

    const compiled = harness.routeNativeElement as HTMLElement | null;
    expect(compiled?.querySelector('h1')?.textContent).toContain('QuotesApi');
  });
});
