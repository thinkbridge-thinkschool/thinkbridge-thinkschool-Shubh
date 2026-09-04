#!/usr/bin/env bash
# Shared helpers for the Day 22 resilience test scripts. Run with Git Bash / any POSIX
# shell. Requires the app to already be running (see README "How to run the demos").
set -uo pipefail

BASE_URL="${BASE_URL:-http://localhost:5177}"
LOG_FILE="${LOG_FILE:-/tmp/quotesapi_day22.log}"

require_server() {
    if ! curl -s -o /dev/null -w '' "$BASE_URL/api/resilience/circuit-state"; then
        echo "ERROR: QuotesApi is not reachable at $BASE_URL. Start it first, e.g.:" >&2
        echo "  dotnet run --urls $BASE_URL > $LOG_FILE 2>&1 &" >&2
        exit 1
    fi
}

circuit_state() {
    curl -s "$BASE_URL/api/resilience/circuit-state"
}

# Prints the log lines appended since $1 (a line count captured with log_mark).
log_since() {
    tail -n +"$1" "$LOG_FILE" | grep -E "\[Resilience\]|DemoDependencyClient|DemoDependency\]"
}

log_mark() {
    wc -l < "$LOG_FILE" | tr -d ' '
}
