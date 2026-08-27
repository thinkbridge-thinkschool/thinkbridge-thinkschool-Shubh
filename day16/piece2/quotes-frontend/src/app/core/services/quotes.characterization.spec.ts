// Characterization tests (Michael Feathers sense): these pin the ACTUAL, OBSERVED
// behavior of the real Day 13 QuotesApi backend (day13/piece1/QuotesApi), not an
// assumed or documented contract. They talk to the live server directly over the
// network with the platform `fetch`, bypassing Angular's HttpClient/TestBed
// entirely, so a shape mismatch here can only mean the backend actually changed,
// not a mocking mistake in this test.
//
// Prerequisite: the real backend must be running locally before this file runs:
//   cd day13/piece1/QuotesApi && dotnet run --urls http://localhost:5177
// (matches src/app/core/api-base-url.ts). If the backend is down every test below
// fails with a connection error, which is the intended, honest failure mode —
// there is no mock to silently keep this suite green.
import { API_BASE_URL } from '../api-base-url';

const SEEDED_EMAIL = 'test@example.com';
const SEEDED_PASSWORD = 'Password123!';

async function login(): Promise<string> {
  const res = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: SEEDED_EMAIL, password: SEEDED_PASSWORD }),
  });
  expect(res.status).toBe(200);
  const body = (await res.json()) as { access_token: string };
  return body.access_token;
}

describe('QuotesApi real backend characterization (day13/piece1/QuotesApi)', () => {
  it('GET /api/quotes?page=&size= returns 200 with a bare Quote[] (no {items,total} wrapper)', async () => {
    const res = await fetch(`${API_BASE_URL}/api/quotes?page=1&size=3`);
    expect(res.status).toBe(200);

    const body: unknown = await res.json();
    expect(Array.isArray(body)).toBe(true);

    const quotes = body as Record<string, unknown>[];
    expect(quotes.length).toBeGreaterThan(0);
    for (const quote of quotes) {
      // Exactly these five keys - the real Quote shape, nothing invented.
      expect(Object.keys(quote).sort()).toEqual(['author', 'id', 'isDeleted', 'text', 'userId']);
      expect(typeof quote['id']).toBe('number');
      expect(typeof quote['author']).toBe('string');
      expect(typeof quote['text']).toBe('string');
      expect(typeof quote['isDeleted']).toBe('boolean');
      expect(typeof quote['userId']).toBe('number');
    }
  });

  it('GET /api/quotes?page=&size= returns 200 with an empty array for an out-of-range page (empty-list state)', async () => {
    const res = await fetch(`${API_BASE_URL}/api/quotes?page=999999&size=5`);
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual([]);
  });

  it('GET /api/quotes/{id} returns 404 for a non-existent id', async () => {
    const res = await fetch(`${API_BASE_URL}/api/quotes/999999999`);
    expect(res.status).toBe(404);
  });

  it('POST /api/quotes without a token returns 401, unauthenticated writes are rejected outright', async () => {
    const res = await fetch(`${API_BASE_URL}/api/quotes`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ author: 'Someone', text: 'Some text' }),
    });
    expect(res.status).toBe(401);
  });

  it('POST /api/quotes with an invalid body returns 400 with the real ASP.NET Core ValidationProblemDetails shape', async () => {
    const token = await login();

    const res = await fetch(`${API_BASE_URL}/api/quotes`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({ author: '', text: 'has a body but no author' }),
    });
    expect(res.status).toBe(400);
    expect(res.headers.get('content-type')).toContain('application/problem+json');

    const problem = (await res.json()) as {
      type: string;
      title: string;
      status: number;
      errors: Record<string, string[]>;
    };
    // Real shape produced by Results.ValidationProblem(...) in Program.cs -
    // NOT a guessed generic {message: string} error.
    expect(problem.title).toBe('One or more validation errors occurred.');
    expect(problem.status).toBe(400);
    expect(problem.errors).toEqual({
      author: ['Author must be between 1 and 200 characters.'],
    });
  });
});
