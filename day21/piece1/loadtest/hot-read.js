// Sustained-load test for the before/after comparison (Day 21).
// Fires a constant number of concurrent virtual users at the SAME quote id for a fixed
// duration, so requests/sec and p99 latency are comparable between the "before"
// (no HybridCache) and "after" (HybridCache + Redis) runs.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5177';
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
  const res = http.get(`${BASE_URL}/api/quotes/${QUOTE_ID}`);
  check(res, { 'status is 200': (r) => r.status === 200 });
}
