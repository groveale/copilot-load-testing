<#
.SYNOPSIS
    Runs the CopilotStudioClientSample smoke test (single interactive conversation).

.DESCRIPTION
    Builds and runs the console sample against the agent/app registration configured in
    src/CopilotStudioClientSample/appsettings.json. On first run, a browser window will
    open for you to sign in as the licensed test user and consent to the
    Copilot Studio.Copilots.Invoke permission. The resulting token is cached to disk
    (mcs_client_console/ next to the built exe) so subsequent runs won't prompt again
    until the token expires.

.EXAMPLE
    .\scripts\smoke-test.ps1
#>

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\CopilotStudioClientSample"

if (-not (Test-Path $projectPath)) {
    throw "Could not find project at $projectPath"
}

Write-Host "Running Copilot Studio smoke test from $projectPath" -ForegroundColor Cyan
Write-Host "A browser window may open for sign-in on first run..." -ForegroundColor Yellow

Push-Location $projectPath
try {
    dotnet run
}
finally {
    Pop-Location
}
