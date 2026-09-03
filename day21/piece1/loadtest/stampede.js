// Stampede test (Day 21): every virtual user fires exactly ONE request, all at once, for
// the same quote id. Run this right after evicting that id's cache entry so every request
// arrives while the key is genuinely uncached — this is what proves (or disproves) that
// HybridCache collapses the resulting concurrent misses into a single factory execution
// instead of one database query per request.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5177';
const QUOTE_ID = __ENV.QUOTE_ID || '1';
const VUS = parseInt(__ENV.VUS || '100', 10);

export const options = {
  scenarios: {
    stampede: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '30s',
    },
  },
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
};

export default function () {
  const res = http.get(`${BASE_URL}/api/quotes/${QUOTE_ID}`);
  check(res, { 'status is 200': (r) => r.status === 200 });
}
