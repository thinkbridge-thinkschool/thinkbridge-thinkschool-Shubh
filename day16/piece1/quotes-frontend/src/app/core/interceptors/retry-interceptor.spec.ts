import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { retryGetInterceptor } from './retry-interceptor';

const URL = 'http://localhost:5177/api/quotes?page=1&size=10';

describe('retryGetInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([retryGetInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('retries a failing GET with exponential backoff (200ms, 400ms) and resolves once it eventually succeeds', async () => {
    const resultPromise = firstValueFrom(http.get<unknown[]>(URL));

    httpMock.expectOne(URL).flush(null, { status: 503, statusText: 'Service Unavailable' });

    // No second attempt yet — must wait out the first backoff window first.
    await vi.advanceTimersByTimeAsync(100);
    httpMock.expectNone(URL);

    await vi.advanceTimersByTimeAsync(150); // total 250ms > 200ms first delay
    httpMock.expectOne(URL).flush(null, { status: 503, statusText: 'Service Unavailable' });

    await vi.advanceTimersByTimeAsync(200);
    httpMock.expectNone(URL);

    await vi.advanceTimersByTimeAsync(300); // total 500ms > 400ms second delay
    const finalReq = httpMock.expectOne(URL);
    finalReq.flush([{ id: 1, author: 'Ada', text: 'Hi', isDeleted: false, userId: 1 }]);

    await expect(resultPromise).resolves.toEqual([
      { id: 1, author: 'Ada', text: 'Hi', isDeleted: false, userId: 1 },
    ]);
  });

  it('gives up after the max retry count and propagates the final error', async () => {
    // Caught immediately so the eventual rejection is never "unhandled"
    // between here and the assertion below, even though it happens
    // synchronously inside the final flush() call further down.
    const resultPromise = firstValueFrom(http.get<unknown[]>(URL)).catch((err: unknown) => err);

    httpMock.expectOne(URL).flush(null, { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(250);
    httpMock.expectOne(URL).flush(null, { status: 500, statusText: 'Server Error' });
    await vi.advanceTimersByTimeAsync(500);
    httpMock.expectOne(URL).flush(null, { status: 500, statusText: 'Server Error' });

    const result = await resultPromise;
    expect(result).toBeInstanceOf(HttpErrorResponse);
    expect(result).toMatchObject({ status: 500 });
  });

  it('does NOT retry a 404 (the server responded; a second attempt cannot change that)', async () => {
    const resultPromise = firstValueFrom(http.get<unknown[]>(URL)).catch((err: unknown) => err);

    httpMock.expectOne(URL).flush(null, { status: 404, statusText: 'Not Found' });

    await vi.advanceTimersByTimeAsync(1000);
    httpMock.expectNone(URL); // no retry attempt was ever made

    expect(await resultPromise).toMatchObject({ status: 404 });
  });

  it('does NOT retry POST /api/quotes, even when the server returns a transient 500', async () => {
    const resultPromise = firstValueFrom(
      http.post('http://localhost:5177/api/quotes', { author: 'Ada', text: 'Hi' }),
    ).catch((err: unknown) => err);

    httpMock.expectOne('http://localhost:5177/api/quotes').flush(null, {
      status: 500,
      statusText: 'Server Error',
    });

    await vi.advanceTimersByTimeAsync(1000);
    httpMock.expectNone('http://localhost:5177/api/quotes'); // exactly one attempt, ever

    expect(await resultPromise).toMatchObject({ status: 500 });
  });

  it('does NOT retry DELETE /api/quotes/{id}, even when the server returns a transient 500', async () => {
    const resultPromise = firstValueFrom(http.delete('http://localhost:5177/api/quotes/1')).catch(
      (err: unknown) => err,
    );

    httpMock.expectOne('http://localhost:5177/api/quotes/1').flush(null, {
      status: 500,
      statusText: 'Server Error',
    });

    await vi.advanceTimersByTimeAsync(1000);
    httpMock.expectNone('http://localhost:5177/api/quotes/1');

    expect(await resultPromise).toMatchObject({ status: 500 });
  });
});
