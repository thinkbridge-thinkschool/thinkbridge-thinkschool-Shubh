import { test, expect } from '@playwright/test';

// The one required Day 31 E2E test: register -> login -> open quotes -> create a
// quote -> verify it appears. Runs a real Chromium browser against the real
// Angular dev server, which makes real HTTP calls to the real Day 31 backend
// (day31/piece1/polish/QuotesApi) and a real (ephemeral, local) SQL Server +
// Redis pair — nothing here is mocked or stubbed.
test('register, login, create a quote, and see it appear in the list', async ({ page }) => {
  const unique = `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
  const email = `e2e-${unique}@test.local`;
  const password = 'E2ePassword123!';
  const author = `E2E Author ${unique}`;
  const text = `E2E quote text created by the golden-path test ${unique}.`;

  // 1. Register a brand-new synthetic user.
  await page.goto('/register');
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await page.locator('#confirmPassword').fill(password);
  await page.getByRole('button', { name: 'Create account' }).click();

  // Registration succeeds and redirects to /login (Auth.registerSuccess effect).
  await expect(page).toHaveURL(/\/login$/);

  // 2. Log in with the account just created.
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await page.getByRole('button', { name: 'Log in' }).click();

  // A successful login redirects to /quotes (Auth.isAuthenticated effect).
  await expect(page).toHaveURL(/\/quotes$/);
  await expect(page.getByRole('heading', { name: 'Quotes' })).toBeVisible();

  // 3. Create a quote (the quote form only renders while authenticated).
  await page.locator('#author').fill(author);
  await page.locator('#text').fill(text);
  await page.getByRole('button', { name: 'Create quote' }).click();

  // The form's own success banner confirms the POST succeeded.
  await expect(page.getByRole('status').filter({ hasText: `Quote by ${author} was created.` })).toBeVisible();

  // 4. Verify the created quote is visibly rendered in the list (onQuoteCreated()
  // reloads the current page from the real backend, so this proves the quote was
  // actually persisted and re-fetched, not just optimistically shown).
  const quoteCard = page.locator('.quote-card', { hasText: text });
  await expect(quoteCard).toBeVisible();
  await expect(quoteCard.locator('.quote-card__author')).toHaveText(`— ${author}`);
  await expect(quoteCard.getByText('Yours')).toBeVisible();
});
