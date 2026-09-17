// Sustained-load test for the Day 31 before/after comparison, adapted from Day 21's
// hot-read.js. Fires a constant number of concurrent virtual users at the SAME quote id
// for a fixed duration, so requests/sec and p99 latency are comparable between the
// "before" (Caching:Enabled=false, straight to Azure-SQL-shaped SQL Server) and "after"
// (HybridCache + Redis) runs.
//
// Day 31 changes from the Day 21 original: the route moved from /api/quotes/{id} to
// /api/v1/quotes/{id} (Day 27 versioning), and the default BASE_URL points at the Day 31
// E2E backend (localhost:5310, run against ephemeral SQL Server/Redis containers) instead
// of the old single-project app on 5177.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5310';
const QUOTE_ID = __ENV.QUOTE_ID || '1';
const VUS = parseInt(__ENV.VUS || '100', 10);
const DURATION = __ENV.DURATION || '15s';

export const options = {
  scenarios: {
    hot_read: {
      executor: 'constant-vus',
      vus: VUS,
      duration: DURATION,
    },
  },
  // p99 isn't in k6's default summary trend stats; the before/after comparison needs it.
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
};

export default function () {
  const res = http.get(`${BASE_URL}/api/v1/quotes/${QUOTE_ID}`);
  check(res, { 'status is 200': (r) => r.status === 200 });
}
