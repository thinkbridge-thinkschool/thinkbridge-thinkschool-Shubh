# Day 21 experiment driver: resets the DB-query and cache-hit/miss counters, optionally
# evicts a quote's cache entry, runs a k6 script against the running QuotesApi, then reads
# the counters back and writes one combined JSON result. Used for all three experiments
# (before, after, stampede) so every run is captured the same reproducible way.
param(
    [string]$BaseUrl = "http://localhost:5177",
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

Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/diagnostics/db-queries/reset" | Out-Null
Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/diagnostics/cache-metrics/reset" | Out-Null

if ($EvictFirst) {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/diagnostics/cache/$QuoteId/evict" | Out-Null
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

$dbQueries = Invoke-RestMethod -Uri "$BaseUrl/api/diagnostics/db-queries"
$cacheMetrics = Invoke-RestMethod -Uri "$BaseUrl/api/diagnostics/cache-metrics"
$k6Summary = Get-Content $k6SummaryPath -Raw | ConvertFrom-Json

$reqDuration = $k6Summary.metrics.http_req_duration
$httpReqs = $k6Summary.metrics.http_reqs

$reportedDuration = if ($Mode -eq "stampede") { "single burst (1 iteration/VU)" } else { $Duration }

$combined = [ordered]@{
    label            = $Label
    mode             = $Mode
    quoteId          = $QuoteId
    concurrentVUs    = $Vus
    duration         = $reportedDuration
    totalRequests    = [int]$httpReqs.count
    requestsPerSec   = [math]::Round($httpReqs.rate, 2)
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
