// Mirrors QuotesApi.Models.Quote exactly (Program.cs GET /api/quotes, GET /api/quotes/{id}).
export interface Quote {
  id: number;
  author: string;
  text: string;
  isDeleted: boolean;
  userId: number;
}

// Mirrors QuotesApi.Models.QuoteCreateRequest (Program.cs POST /api/quotes).
export interface QuoteCreateRequest {
  author: string;
  text: string;
}
