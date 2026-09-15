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

  // GET /api/quotes?page=&size=&author= (Program.cs) — anonymous, paginated, no
  // total-count field exists. `author` is an optional case-insensitive
  // "contains" filter matched server-side against every quote, not just the
  // current page.
  getQuotes(page: number, size: number, author?: string): Observable<Quote[]> {
    const params: Record<string, string | number> = { page, size };
    if (author) {
      params['author'] = author;
    }
    return this.http.get<Quote[]>(`${API_BASE_URL}/api/quotes`, { params });
  }

  // GET /api/quotes/{id} (Program.cs) — anonymous.
  getQuoteById(id: number): Observable<Quote> {
    return this.http.get<Quote>(`${API_BASE_URL}/api/quotes/${id}`);
  }

  // POST /api/quotes (Program.cs) — requires the "can-edit-quotes" policy
  // (JWT "scope" claim = "quotes.write", granted to every logged-in user by /api/auth/login).
  createQuote(request: QuoteCreateRequest): Observable<Quote> {
    return this.http.post<Quote>(`${API_BASE_URL}/api/quotes`, request);
  }

  // DELETE /api/quotes/{id} (Program.cs) — requires the "can-delete-own-quote" policy,
  // satisfied only when the JWT's user id matches the quote's UserId.
  deleteQuote(id: number): Observable<void> {
    return this.http.delete<void>(`${API_BASE_URL}/api/quotes/${id}`);
  }
}
