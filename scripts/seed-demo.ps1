param(
    [string]$BaseUrl,
    [string]$ApiKey
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $BaseUrl) {
    $BaseUrl = if ($env:FFAAAS_API_URL) { $env:FFAAAS_API_URL } else { 'http://localhost:8080' }
}

if (-not $ApiKey) {
    $ApiKey = if ($env:FFAAAS_API_TOKEN) { $env:FFAAAS_API_TOKEN } else { 'dev-editor-token' }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$project = Join-Path $repoRoot 'tools\FfaasLite.AdminCli\FfaasLite.AdminCli.csproj'
$seedDir = Join-Path $scriptRoot 'seed-data'

function Invoke-AdminCli([string[]] $arguments) {
    $cmd = @(
        'run', '--project', $project,
        '--',
        '--url', $BaseUrl,
        '--api-key', $ApiKey
    ) + $arguments

    Write-Host "dotnet $($cmd -join ' ')" -ForegroundColor DarkGray
    dotnet @cmd | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Admin CLI command failed (args: $($arguments -join ' '))."
    }
}

Write-Host "Seeding demo flags against $BaseUrl..." -ForegroundColor Cyan

Invoke-AdminCli @(
    'flags', 'upsert', 'new-ui',
    '--type', 'boolean',
    '--bool-value', 'false',
    '--rules', (Join-Path $seedDir 'new-ui.json')
)

Invoke-AdminCli @(
    'flags', 'upsert', 'checkout',
    '--type', 'boolean',
    '--bool-value', 'false',
    '--rules', (Join-Path $seedDir 'checkout.json')
)

Invoke-AdminCli @(
    'flags', 'upsert', 'ui-ver',
    '--type', 'string',
    '--string-value', 'v1',
    '--rules', (Join-Path $seedDir 'ui-ver.json')
)

Invoke-AdminCli @(
    'flags', 'upsert', 'rate-limit',
    '--type', 'number',
    '--number-value', '50',
    '--rules', (Join-Path $seedDir 'rate-limit.json')
)

Invoke-AdminCli @('flags', 'list')

Write-Host 'Demo seed completed.' -ForegroundColor Green

