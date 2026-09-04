#!/usr/bin/env bash
# Day 22 — circuit breaker: CLOSED -> OPEN -> fail-fast -> HALF-OPEN -> CLOSED.
#
# Run this against a freshly started app (see README) so the breaker starts CLOSED.
#
#   A/B. Repeatedly call the failing dependency until sustained failures open the circuit.
#   C.   Keep calling while OPEN and show the dependency is no longer invoked (fail-fast).
#   D.   Wait out the break duration.
#   E/F. Send a request that becomes the HALF-OPEN probe, against the SUCCESS scenario,
#        so the probe genuinely observes a recovered dependency.
#   G.   Confirm the circuit reports CLOSED again.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ./common.sh
require_server

BREAK_DURATION_SECONDS=8 # must match ResilienceExtensions.CircuitBreakDuration

echo "=== Circuit breaker test ==="
echo "Circuit state at start: $(circuit_state)"
echo

echo "--- Step A/B: driving sustained failures against GET /api/resilience/demo/failure until the circuit opens ---"
MARK=$(log_mark)
STATE="Closed"
for i in $(seq 1 6); do
    RESULT=$(curl -s "$BASE_URL/api/resilience/demo/failure")
    STATE=$(circuit_state)
    echo "  call $i -> $RESULT | circuit state: $STATE"
    if echo "$STATE" | grep -q '"Open"'; then
        break
    fi
done
echo
echo ">>> Circuit is now: $STATE"
log_since "$MARK" | grep -E "OPENED|OnRetry|Retry attempt" | tail -20
echo

echo "--- Step C: calling again 3x while OPEN — must fail fast, dependency must NOT be called ---"
MARK=$(log_mark)
for i in 1 2 3; do
    START=$(date +%s%3N)
    RESULT=$(curl -s "$BASE_URL/api/resilience/demo/failure")
    END=$(date +%s%3N)
    echo "  call $i -> $RESULT (elapsed $((END - START))ms — fast, no retry/backoff wait)"
done
echo "  server log for these calls:"
log_since "$MARK"
DEP_CALLS=$(log_since "$MARK" | grep -c "\[DemoDependency\]" || true)
echo "  outbound calls that actually reached the dependency during these OPEN requests: $DEP_CALLS (expect 0)"
echo

echo "--- Step D: waiting ${BREAK_DURATION_SECONDS}s for the break duration to expire ---"
sleep "$((BREAK_DURATION_SECONDS + 1))"
echo

echo "--- Step E/F: sending the HALF-OPEN probe against the SUCCESS scenario ---"
MARK=$(log_mark)
RESULT=$(curl -s "$BASE_URL/api/resilience/demo/success")
echo "  probe result: $RESULT"
sleep 0.3
echo "  server log for the probe:"
log_since "$MARK"
echo

echo "--- Step G: confirming recovery ---"
FINAL_STATE=$(circuit_state)
echo "Circuit state after successful probe: $FINAL_STATE (expect Closed)"
