param(
 [Parameter(Mandatory=$true)][string]$Zip,
 [Parameter(Mandatory=$true)][string]$Version,
 [Parameter(Mandatory=$true)][string]$DownloadUrl,
 [string]$Executable = 'Game.exe',
 [string]$Output = (Join-Path $PSScriptRoot 'manifest.json')
)
$ErrorActionPreference = 'Stop'
$null = [version]::Parse($Version)
if (([uri]$DownloadUrl).Scheme -ne 'https') { throw 'Use an HTTPS download URL' }
$item = Get-Item -LiteralPath $Zip
@{ version=$Version; url=$DownloadUrl; sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLower(); size=$item.Length; executable=$Executable } | ConvertTo-Json | Set-Content -LiteralPath $Output -Encoding UTF8
Write-Host "Created $Output"
