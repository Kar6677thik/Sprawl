# Starts Sprawl locally with no cluster, in Windows PowerShell 5.1:
#   API  https://localhost:7543  UI listener (owner cookie only; Development auto sign-in; seeded sample canvas)
#        http://127.0.0.1:7544   device listener (MCP device token only)
#   MCP  http://localhost:5181/mcp  (Development bearer from .local/mcp-dev.token; no OAuth issuer needed)
# Creates the device token and the development bearer under .local on first run; neither is printed.
# Run scripts/build-local.ps1 first. Ctrl+C stops both processes.
param(
    [int]$Port = 7543,
    [int]$DevicePort = 7544,
    [int]$McpPort = 5181,
    [string]$VerifierFile,
    [string]$TotpFile,
    [switch]$NoSeed,
    [switch]$NoMcp
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$local = Join-Path $root '.local'
if (-not (Test-Path -LiteralPath $local)) { New-Item -ItemType Directory -Path $local | Out-Null }
if (-not (Test-Path -LiteralPath (Join-Path $local 'mcp-device.sha256'))) { & (Join-Path $PSScriptRoot 'new-device-token.ps1') }
$devBearer = Join-Path $local 'mcp-dev.token'
if (-not (Test-Path -LiteralPath $devBearer)) {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $value = 'spr_local_' + ([Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'))
    [System.IO.File]::WriteAllText($devBearer, $value, (New-Object System.Text.ASCIIEncoding))
    Remove-Variable value, bytes
    Write-Host "Wrote the local MCP bearer to $devBearer."
}
# An MCP client config (Claude Code --mcp-config, MCP Inspector --config) so the bearer never goes on a command line.
$clientConfig = Join-Path $local 'claude-mcp.json'
$bearer = ([System.IO.File]::ReadAllText($devBearer)).Trim()
$json = '{ "mcpServers": { "sprawl": { "type": "http", "url": "http://localhost:' + $McpPort + '/mcp", "headers": { "Authorization": "Bearer ' + $bearer + '" } } } }'
[System.IO.File]::WriteAllText($clientConfig, $json, (New-Object System.Text.ASCIIEncoding))
Remove-Variable bearer, json

$names = @('ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'Api__PublicOrigin', 'Api__DevicePort', 'Api__DatabasePath', 'Api__SeedSample',
    'Auth__DevAutoSignIn', 'Auth__DeviceTokenSha256File', 'Auth__CredentialFile', 'Auth__TotpFile',
    'Mcp__PublicOrigin', 'Mcp__ApiBaseUrl', 'OAuth__Resource', 'OAuth__Audience', 'Development__BearerTokenFile', 'Secrets__ApiDeviceTokenFile')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$api = $null
try {
    dotnet build (Join-Path $root 'Sprawl.slnx') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

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

    if ($NoMcp) {
        Write-Host "Sprawl API: https://localhost:$Port (UI) and http://127.0.0.1:$DevicePort (device). Ctrl+C stops it."
        dotnet run --project (Join-Path $root 'src/Sprawl.Api') --no-launch-profile --no-build
        return
    }
    # The API runs as a child process (it inherits the environment above); the MCP runs in this window.
    # Windows PowerShell 5.1 does not quote array arguments, so a path with spaces must be quoted by hand.
    $apiProject = Join-Path $root 'src/Sprawl.Api'
    $api = Start-Process -FilePath 'dotnet' -ArgumentList "run --project `"$apiProject`" --no-launch-profile --no-build" -NoNewWindow -PassThru

    $env:ASPNETCORE_URLS = "http://localhost:$McpPort"
    $env:Mcp__PublicOrigin = "http://localhost:$McpPort"
    $env:Mcp__ApiBaseUrl = "http://127.0.0.1:$DevicePort"
    $env:OAuth__Resource = "http://localhost:$McpPort/mcp"
    $env:OAuth__Audience = "http://localhost:$McpPort/mcp"
    $env:Development__BearerTokenFile = $devBearer
    $env:Secrets__ApiDeviceTokenFile = (Join-Path $local 'mcp-device.token')
    Write-Host "Sprawl API: https://localhost:$Port (UI), http://127.0.0.1:$DevicePort (device)."
    Write-Host "Sprawl MCP: http://localhost:$McpPort/mcp (client config with the bearer: .local\claude-mcp.json). Ctrl+C stops both."
    dotnet run --project (Join-Path $root 'src/Sprawl.Mcp') --no-launch-profile --no-build
} finally {
    if ($api -and -not $api.HasExited) {
        # dotnet run hosts the app in a child process; stop the whole tree.
        & taskkill.exe /PID $api.Id /T /F | Out-Null
    }
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
