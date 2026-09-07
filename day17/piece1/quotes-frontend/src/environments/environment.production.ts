// Real deployed QuotesApi base URL (Azure Container Apps). Filled in after the
// backend was actually deployed and its live URL verified with curl — never a
// guessed or reused URL from a different day's deployment. The container app
// resource itself is still named "quotes-api-day13-piece1" (see
// day22/piece1/QuotesApi/.azure/day13-piece1-quotesapi/.env), because later
// days' QuotesApi keep redeploying to that same Azure resource rather than
// provisioning a new one — the URL below is that resource's address, not
// necessarily day13's code.
export const environment = {
  production: true,
  apiBaseUrl: 'https://quotes-api-day13-piece1.bluemoss-72267de6.eastasia.azurecontainerapps.io',
};
