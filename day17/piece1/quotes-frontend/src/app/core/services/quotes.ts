import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Quote, QuoteCreateRequest } from '../models/quote.models';
import { API_BASE_URL } from '../api-base-url';

@Injectable({
  providedIn: 'root',
})
export class Quotes {
  private readonly http = inject(HttpClient);

  // GET /api/v1/quotes?page=&size=&author= (Day 29 QuoteEndpoints) — anonymous, paginated, no
  // total-count field exists. `author` is an optional case-insensitive
  // "contains" filter matched server-side against every quote, not just the
  // current page.
  getQuotes(page: number, size: number, author?: string): Observable<Quote[]> {
    const params: Record<string, string | number> = { page, size };
    if (author) {
      params['author'] = author;
    }
    return this.http.get<Quote[]>(`${API_BASE_URL}/api/v1/quotes`, { params });
  }

  // GET /api/v1/quotes/{id} (Day 29 QuoteEndpoints) — anonymous.
  getQuoteById(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${API_BASE_URL}/api/v1/quotes/${id}`);
  }

  // POST /api/v1/quotes (Day 29 QuoteEndpoints) — requires the "can-edit-quotes" policy
  // (JWT "scope" claim = "quotes.write", granted to every logged-in user by /api/v1/auth/login).
  createQuote(request: QuoteCreateRequest): Observable<Quote> {
    return this.http.post<Quote>(`${API_BASE_URL}/api/v1/quotes`, request);
  }

  // DELETE /api/v1/quotes/{id} (Day 29 QuoteEndpoints) — requires the "can-delete-own-quote"
  // policy, satisfied only when the JWT's user id matches the quote's UserId.
  deleteQuote(id: number): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/api/v1/quotes/${id}`);
  }
}
