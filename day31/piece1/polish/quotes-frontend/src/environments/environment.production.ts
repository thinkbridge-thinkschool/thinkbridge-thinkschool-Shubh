// Real deployed QuotesApi base URL (Azure Container Apps). Filled in after the
// backend was actually deployed and its live URL verified with curl — never a
// guessed or reused URL from a different day's deployment.
//
// Day 32: points at the Day 32 PROD Container App (quotes-api-day32-prod, new
// subscription), verified live (GET /openapi/v1.json -> 200) before being set here.
// The DEV build uses environment.azure-dev.ts (`ng build --configuration azure-dev`).
// Previously this pointed at the Day 29 Dev container app (quotes-api-day29-dev) on the
// old, now-disabled subscription.
export const environment = {
  production: true,
  apiBaseUrl: 'https://quotes-api-day32-prod.bravesmoke-5f56c4fd.eastasia.azurecontainerapps.io',
};
