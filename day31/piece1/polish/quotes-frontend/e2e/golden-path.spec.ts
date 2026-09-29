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
  // The form now lives in a modal opened from the navigation rail's "New Quote"
  // button instead of sitting inline under the list — the form, its field ids
  // and its validation are unchanged, it just has to be opened first.
  await page.getByRole('button', { name: 'New Quote' }).click();
  await page.locator('#author').fill(author);
  await page.locator('#text').fill(text);

  // onQuoteCreated() reloads page 1 from the real backend; listen for that
  // re-fetch before submitting so its response can't be missed.
  const listResponse = (pageNumber: number) =>
    page.waitForResponse(
      (res) =>
        res.request().method() === 'GET' &&
        /\/api\/v1\/quotes\?/.test(res.url()) &&
        new URL(res.url()).searchParams.get('page') === String(pageNumber),
    );
  let pageLoaded = listResponse(1);
  await page.getByRole('button', { name: 'Create quote' }).click();

  // The success banner confirms the POST succeeded. It is rendered by the list
  // (from QuoteForm's `created` output) rather than inside the form, because a
  // successful create closes the dialog.
  await expect(page.getByRole('status').filter({ hasText: `Quote by ${author} was created.` })).toBeVisible();

  // 4. Verify the created quote is visibly rendered in the list, re-fetched from the
  // real backend (so it was actually persisted, not just optimistically shown).
  //
  // GET /api/v1/quotes has no ORDER BY, so once a database holds more than one
  // page of quotes the new one is not guaranteed to be on page 1 (a long-lived
  // deployed environment, unlike the empty CI database). Walk the list with the
  // real "Next" control until the card appears: each step waits for that page's
  // actual API response and for the loading skeleton to clear, and "Next" is
  // disabled on the last page, so the walk always ends.
  const quoteCard = page.locator('.quote-card', { hasText: text });
  const nextButton = page.getByRole('button', { name: 'Next ›' });
  for (let pageNumber = 1; ; pageNumber++) {
    expect((await pageLoaded).ok(), `GET quotes page ${pageNumber} should succeed`).toBe(true);
    await expect(page.locator('.pagination__current')).toHaveText(`Page ${pageNumber}`);
    await expect(page.getByRole('status', { name: 'Loading quotes' })).toHaveCount(0);
    if ((await quoteCard.count()) > 0 || (await nextButton.isDisabled())) {
      break;
    }
    pageLoaded = listResponse(pageNumber + 1);
    await nextButton.click();
  }

  await expect(quoteCard).toBeVisible();
  await expect(quoteCard.locator('.quote-card__author')).toHaveText(`— ${author}`);
  await expect(quoteCard.getByText('Yours')).toBeVisible();
});
