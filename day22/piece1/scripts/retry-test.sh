#!/usr/bin/env bash
# Day 22 — retry-with-backoff evidence.
#
# Calls the idempotent GET path against the always-failing dependency once. The pipeline
# retries it 3 times with exponential backoff (200ms, 400ms, 800ms) before giving up, and
# the circuit breaker's minimum throughput (8) is intentionally higher than one call's own
# attempt count (4), so this single call proves retry behaviour in isolation without also
# tripping the breaker.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ./common.sh
require_server

echo "=== Retry-with-backoff test ==="
echo "Circuit state before: $(circuit_state)"

MARK=$(log_mark)
echo "--- Calling GET /api/resilience/demo/failure (idempotent, retryable) ---"
START=$(date +%s%3N)
RESPONSE=$(curl -s -w '\nHTTP %{http_code}\n' "$BASE_URL/api/resilience/demo/failure")
END=$(date +%s%3N)
echo "$RESPONSE"
echo "Elapsed: $((END - START))ms (expect ~1400ms+ = 200+400+800ms backoff plus call time)"

sleep 0.3
echo
echo "--- Server log: attempt / delay / reason for each retry ---"
log_since "$MARK"

echo
echo "Circuit state after: $(circuit_state) (expect Closed — one call's retries alone must not trip the breaker)"
