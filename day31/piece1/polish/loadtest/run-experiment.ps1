# Day 31 experiment driver, adapted from Day 21's run-experiment.ps1.
#
# Two Day 31 changes from the Day 21 original:
#   1. Routes moved under /api/v1 (Day 27 versioning): /api/quotes/{id} ->
#      /api/v1/quotes/{id}, /api/diagnostics/* -> /api/v1/diagnostics/*.
#   2. The diagnostics endpoints now require authentication (RequireAuthorization(),
#      added in the Day 27 security pass) — this script logs in a dedicated local test
#      user first and attaches the resulting JWT as a Bearer token to every diagnostics
#      call. The hot-read endpoint itself (GET /api/v1/quotes/{id}) is still anonymous,
#      so k6 itself needs no token.
param(
    [string]$BaseUrl = "http://localhost:5310",
    [int]$QuoteId = 1,
    [int]$Vus = 100,
    [string]$Duration = "15s",
    [ValidateSet("sustained", "stampede")]
    [string]$Mode = "sustained",
    [switch]$EvictFirst,
    [Parameter(Mandatory = $true)]
    [string]$Label
)

$ErrorActionPreference = "Stop"
$resultsDir = Join-Path $PSScriptRoot "results"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

# A fixed, synthetic, non-production local test account — registered once and reused on
# every run of this script (registering an email that already exists just returns 409,
# which this ignores).
$perfEmail = "perf-test@test.local"
$perfPassword = "PerfTest123!"
try {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/auth/register" `
        -ContentType "application/json" `
        -Body (@{ email = $perfEmail; password = $perfPassword } | ConvertTo-Json) | Out-Null
} catch {
    # 409 Conflict (already registered) is expected on every run after the first.
}
$login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/auth/login" `
    -ContentType "application/json" `
    -Body (@{ email = $perfEmail; password = $perfPassword } | ConvertTo-Json)
$authHeaders = @{ Authorization = "Bearer $($login.access_token)" }

Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/diagnostics/db-queries/reset" -Headers $authHeaders | Out-Null
Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/diagnostics/cache-metrics/reset" -Headers $authHeaders | Out-Null

if ($EvictFirst) {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/diagnostics/cache/$QuoteId/evict" -Headers $authHeaders | Out-Null
}

$script = if ($Mode -eq "stampede") { "stampede.js" } else { "hot-read.js" }
$k6SummaryPath = Join-Path $resultsDir "$Label-k6-summary.json"

$k6Args = @(
    "run",
    "-e", "BASE_URL=$BaseUrl",
    "-e", "QUOTE_ID=$QuoteId",
    "-e", "VUS=$Vus",
    "-e", "DURATION=$Duration",
    "--summary-export=$k6SummaryPath",
    (Join-Path $PSScriptRoot $script)
)

& k6 @k6Args
if ($LASTEXITCODE -ne 0) {
    throw "k6 run failed with exit code $LASTEXITCODE"
}

$dbQueries = Invoke-RestMethod -Uri "$BaseUrl/api/v1/diagnostics/db-queries" -Headers $authHeaders
$cacheMetrics = Invoke-RestMethod -Uri "$BaseUrl/api/v1/diagnostics/cache-metrics" -Headers $authHeaders
$k6Summary = Get-Content $k6SummaryPath -Raw | ConvertFrom-Json

$reqDuration = $k6Summary.metrics.http_req_duration
$httpReqs = $k6Summary.metrics.http_reqs
$httpReqFailed = $k6Summary.metrics.http_req_failed

$reportedDuration = if ($Mode -eq "stampede") { "single burst (1 iteration/VU)" } else { $Duration }

$combined = [ordered]@{
    label            = $Label
    mode             = $Mode
    quoteId          = $QuoteId
    concurrentVUs    = $Vus
    duration         = $reportedDuration
    totalRequests    = [int]$httpReqs.count
    requestsPerSec   = [math]::Round($httpReqs.rate, 2)
    errorRate        = if ($httpReqFailed) { [math]::Round($httpReqFailed.value, 4) } else { 0 }
    p50LatencyMs     = [math]::Round($reqDuration.med, 2)
    p95LatencyMs     = [math]::Round($reqDuration."p(95)", 2)
    p99LatencyMs     = [math]::Round($reqDuration."p(99)", 2)
    avgLatencyMs     = [math]::Round($reqDuration.avg, 2)
    dbTotalQueries   = $dbQueries.totalQueries
    dbQueriesPerSec  = $dbQueries.queriesPerSecond
    cacheHits        = $cacheMetrics.hits
    cacheMisses      = $cacheMetrics.misses
    cacheTotal       = $cacheMetrics.total
    cacheHitRate     = $cacheMetrics.hitRate
}

$resultPath = Join-Path $resultsDir "$Label.json"
$combined | ConvertTo-Json | Set-Content -Path $resultPath -Encoding utf8

Write-Host "`n=== $Label result ===" -ForegroundColor Cyan
$combined | Format-List
Write-Host "Saved to $resultPath"
