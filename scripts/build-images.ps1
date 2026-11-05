# Builds both release images for Megatron (linux/amd64) and saves them as tars under .local\images, ready to copy
# and import (docs/02-operations.md §1-2). Runs in Windows PowerShell 5.1 with Docker Desktop's Linux engine.
param([string]$Tag = 'candidate')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root '.local\images'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$images = @(
    @{ Name = 'sprawl'; Dockerfile = 'Dockerfile' },
    @{ Name = 'sprawl-mcp'; Dockerfile = 'Dockerfile.mcp' }
)
Push-Location $root
try {
    foreach ($image in $images) {
        $reference = "ghcr.io/kar6677thik/$($image.Name):$Tag"
        docker build --platform linux/amd64 -f $image.Dockerfile -t $reference .
        if ($LASTEXITCODE -ne 0) { throw "docker build failed for $reference." }
        $tar = Join-Path $out "$($image.Name).tar"
        docker save $reference -o $tar
        if ($LASTEXITCODE -ne 0) { throw "docker save failed for $reference." }
        Write-Host ("{0} -> {1} ({2:N0} MB)" -f $reference, $tar, ((Get-Item $tar).Length / 1MB))
    }
} finally { Pop-Location }
Write-Host 'Next: copy the tars and deploy/import-image.sh to Megatron and import them (docs/02-operations.md section 2).'
