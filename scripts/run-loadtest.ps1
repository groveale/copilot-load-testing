<#
.SYNOPSIS
    Runs a multi-user load-test pilot against the configured Copilot Studio agent.

.DESCRIPTION
    Signs in each user listed under LoadTestSettings:Users (in
    src/LoadTestDriver/appsettings.json) one at a time via device code, then runs their
    message sessions concurrently (capped by LoadTestSettings:MaxConcurrentUsers, 0 = all).
    Each user sends LoadTestSettings:MessagesPerUser messages, one at a time, picked
    randomly from LoadTestSettings:PromptBank. Results (user, conversation ID, client-side
    timing, best-effort answered/refused classification) are written to a timestamped CSV
    under the repo-root output\ folder.

    This uses the same app registration as the smoke test, but a separate token cache per
    user (mcs_loadtest_cache\<user>\). Each user needs its own one-time interactive/device-
    code sign-in the first time it's used.

.EXAMPLE
    .\scripts\run-loadtest.ps1
#>

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\LoadTestDriver"

if (-not (Test-Path $projectPath)) {
    throw "Could not find project at $projectPath"
}

Write-Host "Running Copilot Studio load test pilot from $projectPath" -ForegroundColor Cyan

Push-Location $projectPath
try {
    dotnet run
}
finally {
    Pop-Location
}
