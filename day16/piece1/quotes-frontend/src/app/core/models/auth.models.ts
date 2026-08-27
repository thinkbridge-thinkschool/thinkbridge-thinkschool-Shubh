// Mirrors QuotesApi.Models.LoginRequest (Program.cs POST /api/auth/login).
export interface LoginRequest {
  email: string;
  password: string;
}

// Mirrors the anonymous object returned by POST /api/auth/login and POST /api/auth/refresh
// in Program.cs. Field names are snake_case exactly as written by the backend.
export interface TokenResponse {
  access_token: string;
  refresh_token: string;
  expires_in: number;
}

// Mirrors QuotesApi.Models.RefreshRequest (Program.cs POST /api/auth/refresh and /api/auth/logout).
export interface RefreshRequest {
  refreshToken: string;
}
