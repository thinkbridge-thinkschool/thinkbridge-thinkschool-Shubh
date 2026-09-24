// Day 31 local/E2E default. Points at the Day 31 backend
// (day31/piece1/polish/QuotesApi) run locally — never Azure, and never another
// day's backend.
//
// 5177 is that project's own launchSettings.json `http` profile port, i.e. what
// a bare `dotnet run` actually binds. This used to say 5310, which nothing
// bound unless the backend was started with an explicit
// `--urls http://localhost:5310`, so a plain `dotnet run` left every request
// failing with ECONNREFUSED. api-base-url.ts, quotes.characterization.spec.ts
// and the interceptor specs all already assumed 5177.
//
// If you do start the backend on a different port, update it here to match.
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5177',
};
