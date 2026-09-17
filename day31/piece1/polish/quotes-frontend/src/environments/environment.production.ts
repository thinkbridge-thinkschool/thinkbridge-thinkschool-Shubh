// Real deployed QuotesApi base URL (Azure Container Apps). Filled in after the
// backend was actually deployed and its live URL verified with curl — never a
// guessed or reused URL from a different day's deployment.
//
// Day 29 Stage 3A: points at the Day 29 Dev container app (quotes-api-day29-dev),
// which is a dedicated Azure SQL database (quotesapi-day29) + Managed Identity
// auth, not SQLite and not the old Day 13-era backend this used to point at. This
// is still the Dev environment — repoint this again once Day 29 Prod exists.
export const environment = {
  production: true,
  apiBaseUrl: 'https://quotes-api-day29-dev.bluemoss-72267de6.eastasia.azurecontainerapps.io',
};
