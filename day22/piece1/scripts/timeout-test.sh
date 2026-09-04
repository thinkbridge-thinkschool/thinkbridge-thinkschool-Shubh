#!/usr/bin/env bash
# Day 22 — timeout evidence.
#
# Calls the /demo/slow scenario, which hangs for 6s server-side. The pipeline's Timeout
# strategy is configured for 3s, so the call must be cancelled well before the dependency
# would otherwise respond, and the client must see that failure rather than waiting.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
source ./common.sh
require_server

echo "=== Timeout test ==="
MARK=$(log_mark)
echo "--- Calling GET /api/resilience/demo/slow (dependency delays 6000ms, pipeline timeout is 3000ms) ---"
START=$(date +%s%3N)
RESPONSE=$(curl -s -w '\nHTTP %{http_code}\n' "$BASE_URL/api/resilience/demo/slow")
END=$(date +%s%3N)
echo "$RESPONSE"
echo "Elapsed: $((END - START))ms (expect ~3000ms, NOT ~6000ms)"

sleep 0.3
echo
echo "--- Server log: outbound call started -> timeout -> cancelled ---"
log_since "$MARK"
