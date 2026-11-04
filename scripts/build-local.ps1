# Restores (locked) and builds everything: the web UI into src/Sprawl.Api/wwwroot, then the .NET solution.
# Runs in Windows PowerShell 5.1. Needs Node 24.15+ with npm for the web build (skip it with -SkipWeb).
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$SkipWeb
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $SkipWeb) {
        Push-Location (Join-Path $root 'src/Sprawl.Web')
        try {
            npm ci --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
            npm run build
            if ($LASTEXITCODE -ne 0) { throw 'The web build failed.' }
        } finally { Pop-Location }
    }
    dotnet restore Sprawl.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    dotnet build Sprawl.slnx -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
} finally { Pop-Location }
