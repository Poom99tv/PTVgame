# Build Game To Earn (Windows) -> ..\GameToEarn\PTVGameToEarn.exe (+ a zip for GitHub Releases)
# Usage:  powershell -ExecutionPolicy Bypass -File gametoearn\build.ps1 [-Version 1.0.0]
param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
python -m pip install -r (Join-Path $here 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'pip install failed' }
$work = Join-Path $env:TEMP 'ptv-gte-build'
$pyArgs = @('--noconfirm','--clean','--noconsole','--onedir','--name','PTVGameToEarn','--distpath',$work,'--workpath',(Join-Path $work 'build'),'--specpath',$work,'--collect-all','TikTokLive','--collect-all','edge_tts','--hidden-import','pyttsx3.drivers.sapi5')
if (Test-Path (Join-Path $here 'gifts')) { $pyArgs += @('--add-data', ((Join-Path $here 'gifts') + ';gifts')) }
if (Test-Path (Join-Path $here 'app.ico')) { $pyArgs += @('--icon',(Join-Path $here 'app.ico'),'--add-data',((Join-Path $here 'app.ico') + ';.')) }
$pyArgs += (Join-Path $here 'ptv_game_to_earn.py')
python -m PyInstaller @args
if ($LASTEXITCODE -ne 0) { throw 'PyInstaller failed' }
# local copy next to the Launcher (the Launcher uses this first)
$target = Join-Path $root 'GameToEarn'
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Copy-Item (Join-Path $work 'PTVGameToEarn') $target -Recurse
# release zip + manifest (upload the zip to the launcher release, then commit release-manifests/gametoearn.json)
$zip = Join-Path $root ("GameToEarn-v$Version.zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zip
& (Join-Path $root 'publish-manifest.ps1') -Zip $zip -Version $Version -DownloadUrl ("https://github.com/Poom99tv/PTVgame/releases/download/launcher-v1.9.0/GameToEarn-v$Version.zip") -Executable 'PTVGameToEarn.exe' -Output (Join-Path $root 'release-manifests\gametoearn.json')
Write-Host "Game To Earn ready: $target\PTVGameToEarn.exe"
