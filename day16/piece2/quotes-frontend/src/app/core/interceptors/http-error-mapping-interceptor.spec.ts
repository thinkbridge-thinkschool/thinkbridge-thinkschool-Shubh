import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AppError } from '../models/app-error.models';
import { httpErrorMappingInterceptor } from './http-error-mapping-interceptor';

const URL = 'http://localhost:5177/api/quotes';

describe('httpErrorMappingInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([httpErrorMappingInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('leaves a successful response untouched', async () => {
    const resultPromise = firstValueFrom(http.get(URL));
    httpMock.expectOne(URL).flush([{ id: 1, author: 'Ada', text: 'Hi', isDeleted: false, userId: 1 }]);
    await expect(resultPromise).resolves.toEqual([
      { id: 1, author: 'Ada', text: 'Hi', isDeleted: false, userId: 1 },
    ]);
  });

  it('turns a real ValidationProblemDetails 400 into a typed AppError, not a raw HttpErrorResponse', async () => {
    const resultPromise = firstValueFrom(
      http.post(URL, { author: '', text: 'x' }),
    ).catch((err: unknown) => err);

    httpMock.expectOne(URL).flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { author: ['Author must be between 1 and 200 characters.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    const err = (await resultPromise) as AppError;
    expect(err.kind).toBe('validation');
    expect(err.message).toBe('Author must be between 1 and 200 characters.');
    expect('status' in err).toBe(false); // HttpErrorResponse's shape must not leak through
  });
});
