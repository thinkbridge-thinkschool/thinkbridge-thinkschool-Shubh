// Day 31 E2E default. Points at the Day 31 backend (day31/piece1/polish/QuotesApi)
// run locally via `dotnet run` against dedicated ephemeral SQL Server/Redis
// Testcontainers-style Docker containers — never Azure, and never the day30-sql/
// day29-redis containers or the port-5300/4200 dev servers already running
// locally for other days. Update the port here to match whatever --urls the
// local E2E run actually uses.
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5310',
};
