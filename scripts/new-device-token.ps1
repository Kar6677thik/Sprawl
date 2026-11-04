# Creates the MCP device token and its SHA-256 hash. Neither value is printed.
#   token  -> given to Sprawl.Mcp only (Secrets__ApiDeviceToken, or a file locally)
#   sha256 -> given to Sprawl.Api only (Auth__DeviceTokenSha256File); the API never stores the token itself
# Runs in Windows PowerShell 5.1 and PowerShell 7.
param(
    [string]$TokenFile = (Join-Path (Split-Path -Parent $PSScriptRoot) '.local/mcp-device.token'),
    [string]$HashFile = (Join-Path (Split-Path -Parent $PSScriptRoot) '.local/mcp-device.sha256'),
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
if (((Test-Path -LiteralPath $TokenFile) -or (Test-Path -LiteralPath $HashFile)) -and -not $Force) {
    throw "A device token already exists at $TokenFile. Pass -Force to replace it (the MCP must then be given the new token)."
}
foreach ($file in @($TokenFile, $HashFile)) {
    $directory = Split-Path -Parent $file
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
}
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$token = 'spr_dev_' + ([Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'))
$sha = [System.Security.Cryptography.SHA256]::Create()
try { $hash = -join ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($token)) | ForEach-Object { $_.ToString('x2') }) } finally { $sha.Dispose() }
$ascii = New-Object System.Text.ASCIIEncoding
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($TokenFile), $token, $ascii)
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($HashFile), $hash, $ascii)
Remove-Variable token, bytes
Write-Host "Wrote the device token to $TokenFile and its hash to $HashFile."
