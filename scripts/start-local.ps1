# Starts Sprawl locally with no cluster: the API on https://localhost:7543 (UI listener, cookie only) and
# http://127.0.0.1:7544 (device listener, MCP token only), Development auto sign-in, and the seeded sample canvas.
# Creates the device token under .local on first run. Runs in Windows PowerShell 5.1. Ctrl+C stops it.
# M3 adds the MCP server to this script.
param(
    [int]$Port = 7543,
    [int]$DevicePort = 7544,
    [string]$VerifierFile,
    [string]$TotpFile,
    [switch]$NoSeed
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$local = Join-Path $root '.local'
if (-not (Test-Path -LiteralPath (Join-Path $local 'mcp-device.sha256'))) { & (Join-Path $PSScriptRoot 'new-device-token.ps1') }
$names = @('ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'Api__PublicOrigin', 'Api__DevicePort', 'Api__DatabasePath', 'Api__SeedSample',
    'Auth__DevAutoSignIn', 'Auth__DeviceTokenSha256File', 'Auth__CredentialFile', 'Auth__TotpFile')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "https://localhost:$Port;http://127.0.0.1:$DevicePort"
    $env:Api__PublicOrigin = "https://localhost:$Port"
    $env:Api__DevicePort = "$DevicePort"
    $env:Api__DatabasePath = (Join-Path $local 'sprawl.db')
    $env:Api__SeedSample = $(if ($NoSeed) { 'false' } else { 'true' })
    $env:Auth__DevAutoSignIn = 'true'
    $env:Auth__DeviceTokenSha256File = (Join-Path $local 'mcp-device.sha256')
    if ($VerifierFile) { $env:Auth__CredentialFile = (Resolve-Path -LiteralPath $VerifierFile).Path }
    if ($TotpFile) { $env:Auth__TotpFile = (Resolve-Path -LiteralPath $TotpFile).Path }
    Write-Host "Sprawl API: https://localhost:$Port (UI) and http://127.0.0.1:$DevicePort (device). Ctrl+C stops it."
    dotnet run --project (Join-Path $root 'src/Sprawl.Api') --no-launch-profile
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
