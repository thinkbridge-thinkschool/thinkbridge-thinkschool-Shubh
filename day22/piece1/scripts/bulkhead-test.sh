#!/usr/bin/env bash
# Day 22 — bulkhead/concurrency limiter evidence.
#
# Fires 20 concurrent requests against the slow scenario. The pipeline's concurrency
# limiter allows at most 5 outbound calls in flight at once with no queue, so only 5 of the
# 20 should ever actually reach the dependency; the other 15 must be rejected immediately
# (HTTP 429) rather than being queued or allowed through.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ./common.sh
require_server

CONCURRENT_REQUESTS=20
OUT_DIR=$(mktemp -d)
MARK=$(log_mark)

echo "=== Bulkhead/concurrency-limit test ==="
echo "Firing $CONCURRENT_REQUESTS concurrent GET /api/resilience/demo/slow requests (limit = 5, queue = 0)..."

for i in $(seq 1 "$CONCURRENT_REQUESTS"); do
    curl -s -o "$OUT_DIR/$i.json" -w "%{http_code}" "$BASE_URL/api/resilience/demo/slow" > "$OUT_DIR/$i.code" &
done
wait

echo
echo "--- Response status codes across all $CONCURRENT_REQUESTS requests ---"
sort "$OUT_DIR"/*.code | uniq -c

ACCEPTED=$(grep -l 200 "$OUT_DIR"/*.code 2>/dev/null | wc -l)
REJECTED=$(grep -l 429 "$OUT_DIR"/*.code 2>/dev/null | wc -l)
TIMED_OUT=$(grep -l 504 "$OUT_DIR"/*.code 2>/dev/null | wc -l)
echo "200 (success): $ACCEPTED | 504 (admitted, then timed out): $TIMED_OUT | 429 (bulkhead rejected): $REJECTED"
echo "Admitted into the dependency call (200 + 504): $((ACCEPTED + TIMED_OUT)) (expect 5)"

sleep 0.3
echo
echo "--- Server log: bulkhead rejections vs admitted outbound calls ---"
log_since "$MARK" | grep -c "bulkhead concurrency limit reached" | xargs echo "Rejection log lines:"
log_since "$MARK" | grep -c "\[DemoDependency\] /demo/slow called" | xargs echo "Actual outbound calls that reached the dependency:"

rm -rf "$OUT_DIR"
