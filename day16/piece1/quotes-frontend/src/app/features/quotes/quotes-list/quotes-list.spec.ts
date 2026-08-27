import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../../../core/api-base-url';
import { httpErrorMappingInterceptor } from '../../../core/interceptors/http-error-mapping-interceptor';
import { QuotesList } from './quotes-list';

// Covers the UI/API states required for Day 15: loading, successful data,
// empty list, a normal error, and a mapped-to-friendly error message
// reaching the template. Uses the same httpErrorMappingInterceptor as
// production (app.config.ts) so "friendly message" here is the real mapped
// AppError.message, not a value invented by the test.
describe('QuotesList states', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuotesList],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([httpErrorMappingInterceptor])),
        provideHttpClientTesting(),
        // QuotesList's "View details" link uses [routerLink], which injects
        // ActivatedRoute — a router must be configured for it to resolve.
        provideRouter([]),
      ],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function expectQuotesRequest() {
    return httpMock.expectOne((req) => req.url === `${API_BASE_URL}/api/quotes`);
  }

  it('shows the loading skeleton while the initial GET /api/quotes request is in flight', () => {
    const fixture = TestBed.createComponent(QuotesList);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.list-skeleton')).not.toBeNull();
    expect(el.querySelector('.quote-card')).toBeNull();

    expectQuotesRequest().flush([]);
  });

  it('renders quote cards once the GET succeeds with data', async () => {
    const fixture = TestBed.createComponent(QuotesList);
    fixture.detectChanges();

    expectQuotesRequest().flush([
      { id: 1, author: 'Ada Lovelace', text: 'The Analytical Engine weaves...', isDeleted: false, userId: 1 },
      { id: 2, author: 'Grace Hopper', text: 'A ship in port is safe...', isDeleted: false, userId: 2 },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const cards = el.querySelectorAll('.quote-card');
    expect(cards.length).toBe(2);
    expect(el.textContent).toContain('Ada Lovelace');
    expect(el.textContent).toContain('Grace Hopper');
  });

  it('shows the empty-list state when GET succeeds with an empty array (a real, valid response - not an error)', async () => {
    const fixture = TestBed.createComponent(QuotesList);
    fixture.detectChanges();

    expectQuotesRequest().flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.state--empty')).not.toBeNull();
    expect(el.textContent).toContain('No quotes on this page yet.');
    expect(el.querySelector('.alert--error')).toBeNull();
  });

  it('on a failed GET, shows the alert with the mapped AppError friendly message (not a raw status code) and a retry button', async () => {
    const fixture = TestBed.createComponent(QuotesList);
    fixture.detectChanges();

    expectQuotesRequest().flush(
      { status: 500, title: 'An unexpected error occurred.' },
      { status: 500, statusText: 'Internal Server Error' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.state--error')).not.toBeNull();
    // Exactly the friendly message toAppError produced from the real ProblemDetails
    // title - proves the mapped error actually reaches the template.
    expect(el.textContent).toContain('An unexpected error occurred.');
    expect(el.querySelector('.state--error button')?.textContent).toContain('Retry');

    // Retry re-issues the same request.
    (el.querySelector('.state--error button') as HTMLButtonElement).click();
    expectQuotesRequest().flush([]);
  });
});
