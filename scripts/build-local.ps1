# Restores (locked) and builds everything. Runs in Windows PowerShell 5.1.
# M2 adds the web build (npm ci + vite build into src/Sprawl.Api/wwwroot).
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet restore Sprawl.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    dotnet build Sprawl.slnx -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
} finally { Pop-Location }
