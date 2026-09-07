// Development default. Matches the real QuotesApi backend run locally via
// `dotnet run` in day22/piece1/QuotesApi (see Properties/launchSettings.json) —
// the same port (5177) day13/piece1/QuotesApi originally used, so this URL
// still applies as the backend has moved forward day by day.
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5177',
};
