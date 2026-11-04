# Rebuild assets\gametoearn-engine.zip (TikTok LIVE engine used by the Launcher's Game To Earn page)
# Usage: powershell -ExecutionPolicy Bypass -File gametoearn\build-engine.ps1   (needs Node.js + npm)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'engine'
$stage = Join-Path $env:TEMP 'ptv-gte-engine'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($f in @('engine.mjs','gifts.mjs','image-match.mjs','overlay.mjs','jar-physics.mjs','gift-catalog.json','package.json','package-lock.json','version.txt')) { Copy-Item (Join-Path $src $f) $stage }
Push-Location $stage
try { npm ci --omit=dev --os=win32 --cpu=x64; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' } } finally { Pop-Location }
foreach ($d in @('node_modules\@img\sharp-wasm32','node_modules\@emnapi')) { $p = Join-Path $stage $d; if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
$zip = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\gametoearn-engine.zip'
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Engine package: $zip  (bump engine\version.txt so Launchers re-extract it)"
