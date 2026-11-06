# Rebuild assets\gametoearn-engine.zip (TikTok LIVE engine used by the Launcher's Game To Earn page)
# Usage: powershell -ExecutionPolicy Bypass -File gametoearn\build-engine.ps1   (needs Node.js + npm)
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'engine'
$stageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../dist'))
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
$stage = Join-Path $stageRoot ('gte-engine-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($f in @('engine.mjs','runner.mjs','gifts.mjs','image-match.mjs','overlay.mjs','jar-physics.mjs','gift-catalog.json','package.json','package-lock.json','version.txt')) { Copy-Item (Join-Path $src $f) $stage }
Push-Location $stage
try { npm ci --omit=dev --os=win32 --cpu=x64; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' } } finally { Pop-Location }
foreach ($d in @('node_modules\@img\sharp-wasm32','node_modules\@emnapi')) { $p = [IO.Path]::GetFullPath((Join-Path $stage $d)); if (-not $p.StartsWith($stage + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Path outside engine stage' }; if (Test-Path $p) { Remove-Item -LiteralPath $p -Recurse -Force } }
$zip = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\gametoearn-engine.zip'
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Engine package: $zip  (bump engine\version.txt so Launchers re-extract it)"

# Include the checksum-pinned tunnel used by the PTVRunner relay.
$cloudflared = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/cloudflared.exe'
$expected = '76b2805253d0d6ffacb092a7acfbee99e7af6917835c0c5410e23f3ef5052da2'
if (-not (Test-Path $cloudflared) -or (Get-FileHash $cloudflared -Algorithm SHA256).Hash.ToLower() -ne $expected) {
 Invoke-WebRequest 'https://github.com/cloudflare/cloudflared/releases/download/2026.10.3/cloudflared-windows-amd64.exe' -OutFile $cloudflared
 if ((Get-FileHash $cloudflared -Algorithm SHA256).Hash.ToLower() -ne $expected) { throw 'cloudflared checksum mismatch' }
}
