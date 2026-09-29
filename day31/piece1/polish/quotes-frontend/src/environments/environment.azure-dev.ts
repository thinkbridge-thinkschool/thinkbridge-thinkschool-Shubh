// Day 32 Azure DEV build (`ng build --configuration azure-dev`). Points at the Day 32 DEV
// Container App (quotes-api-day32-dev). Filled in only after DEV was provisioned and this URL
// was verified live (GET /openapi/v1.json -> 200) — never a guessed or reused URL from a
// different day's deployment.
export const environment = {
  production: true,
  apiBaseUrl: 'https://quotes-api-day32-dev.bravesmoke-5f56c4fd.eastasia.azurecontainerapps.io',
};
