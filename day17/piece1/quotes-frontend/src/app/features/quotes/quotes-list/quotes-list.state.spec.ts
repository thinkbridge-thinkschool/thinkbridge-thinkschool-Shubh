import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../../../core/api-base-url';
import { httpErrorMappingInterceptor } from '../../../core/interceptors/http-error-mapping-interceptor';
import { QuotesListState } from './quotes-list.state';

// Unit-level coverage of the signal-based feature state in isolation from the
// component/template — mirrors the DOM-level states already covered by
// quotes-list.spec.ts, plus the concurrency guard that spec can't easily
// exercise (it can only observe the last flush, not intermediate signal
// values between two in-flight requests).
describe('QuotesListState', () => {
  let httpMock: HttpTestingController;
  let state: QuotesListState;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([httpErrorMappingInterceptor])),
        provideHttpClientTesting(),
        QuotesListState,
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    state = TestBed.inject(QuotesListState);
  });

  afterEach(() => httpMock.verify());

  function expectQuotesRequest(page: number, size: number) {
    return httpMock.expectOne(
      (req) =>
        req.url === `${API_BASE_URL}/api/quotes` &&
        req.params.get('page') === String(page) &&
        req.params.get('size') === String(size),
    );
  }

  it('starts in the loading state before any request resolves', () => {
    expect(state.status()).toBe('loading');
    expect(state.quotes()).toEqual([]);
    expect(state.errorMessage()).toBeNull();
  });

  it('transitions to loading immediately when load() is called', () => {
    state.load(1, 10);
    expect(state.status()).toBe('loading');
    expectQuotesRequest(1, 10).flush([]);
  });

  it('exposes the real Quote fields on success (id, author, text, isDeleted, userId)', () => {
    state.load(1, 10);
    expectQuotesRequest(1, 10).flush([
      { id: 1, author: 'Ada Lovelace', text: 'The Analytical Engine weaves...', isDeleted: false, userId: 1 },
    ]);

    expect(state.status()).toBe('loaded');
    expect(state.quotes()).toEqual([
      { id: 1, author: 'Ada Lovelace', text: 'The Analytical Engine weaves...', isDeleted: false, userId: 1 },
    ]);
    expect(state.isEmpty()).toBe(false);
  });

  it('derives the empty state from a real, valid empty array response (not an error)', () => {
    state.load(999999, 5);
    expectQuotesRequest(999999, 5).flush([]);

    expect(state.status()).toBe('loaded');
    expect(state.quotes()).toEqual([]);
    expect(state.isEmpty()).toBe(true);
    expect(state.errorMessage()).toBeNull();
  });

  it('surfaces a mapped, friendly error message on a failed GET', () => {
    state.load(1, 10);
    expectQuotesRequest(1, 10).flush(
      { status: 500, title: 'An unexpected error occurred.' },
      { status: 500, statusText: 'Internal Server Error' },
    );

    expect(state.status()).toBe('error');
    expect(state.errorMessage()).toBe('An unexpected error occurred.');
    expect(state.quotes()).toEqual([]);
  });

  it('recovers on retry: a second load() after an error can succeed', () => {
    state.load(1, 10);
    expectQuotesRequest(1, 10).flush(
      { status: 500, title: 'An unexpected error occurred.' },
      { status: 500, statusText: 'Internal Server Error' },
    );
    expect(state.status()).toBe('error');

    state.load(1, 10);
    expect(state.status()).toBe('loading');
    expectQuotesRequest(1, 10).flush([
      { id: 1, author: 'Ada Lovelace', text: 'Weaves', isDeleted: false, userId: 1 },
    ]);

    expect(state.status()).toBe('loaded');
    expect(state.quotes().length).toBe(1);
    expect(state.errorMessage()).toBeNull();
  });

  it('does not let a stale, slower request overwrite a newer one (older resolves after newer)', () => {
    state.load(1, 10); // page 1 request goes out first...
    const firstReq = expectQuotesRequest(1, 10);

    state.load(2, 10); // ...but the user flips to page 2 before it resolves
    const secondReq = expectQuotesRequest(2, 10);

    // Newer request (page 2) resolves first.
    secondReq.flush([{ id: 2, author: 'Grace Hopper', text: 'Page 2 quote', isDeleted: false, userId: 2 }]);
    expect(state.quotes()).toEqual([
      { id: 2, author: 'Grace Hopper', text: 'Page 2 quote', isDeleted: false, userId: 2 },
    ]);

    // Older request (page 1) resolves after — must be ignored, not overwrite page 2's data.
    firstReq.flush([{ id: 1, author: 'Ada Lovelace', text: 'Page 1 quote', isDeleted: false, userId: 1 }]);
    expect(state.status()).toBe('loaded');
    expect(state.quotes()).toEqual([
      { id: 2, author: 'Grace Hopper', text: 'Page 2 quote', isDeleted: false, userId: 2 },
    ]);
  });

  it('does not let a stale request that errors after a newer success flip the state back to error', () => {
    state.load(1, 10);
    const firstReq = expectQuotesRequest(1, 10);

    state.load(2, 10);
    const secondReq = expectQuotesRequest(2, 10);

    secondReq.flush([{ id: 2, author: 'Grace Hopper', text: 'Page 2 quote', isDeleted: false, userId: 2 }]);
    expect(state.status()).toBe('loaded');

    firstReq.flush(
      { status: 500, title: 'An unexpected error occurred.' },
      { status: 500, statusText: 'Internal Server Error' },
    );
    expect(state.status()).toBe('loaded');
    expect(state.errorMessage()).toBeNull();
  });
});
