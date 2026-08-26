import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './core/interceptors/auth-interceptor';
import { httpErrorMappingInterceptor } from './core/interceptors/http-error-mapping-interceptor';
import { retryGetInterceptor } from './core/interceptors/retry-interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    // Order matters: interceptors nest in array order (first = outermost).
    // retryGetInterceptor must sit closest to the backend so it evaluates
    // retries against the raw HttpErrorResponse; httpErrorMappingInterceptor
    // then converts whatever error survives retries into an AppError before
    // it reaches auth/application code.
    provideHttpClient(
      withInterceptors([authInterceptor, httpErrorMappingInterceptor, retryGetInterceptor]),
    ),
  ],
};
