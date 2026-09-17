// The real QuotesApi backend base URL. Resolved per build configuration via
// angular.json fileReplacements: src/environments/environment.ts (dev,
// localhost:5177) is swapped for environment.production.ts (deployed Azure
// Container Apps URL) in the production build.
import { environment } from '../../environments/environment';

export const API_BASE_URL = environment.apiBaseUrl;
