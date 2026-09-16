// Development default. Points at the Day 30 backend (day30/piece1/feature-
// completeness/QuotesApi) run locally via `dotnet run`, against the local
// SQL Server/Redis containers set up for that stage — never Azure. Update
// the port here to match whatever ASPNETCORE_URLS the local run actually uses.
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5300',
};
