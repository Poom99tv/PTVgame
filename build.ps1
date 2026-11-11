param([string]$OutputName = 'PTVgame-Launcher.exe')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','System.Xaml.dll','System.Security.dll')
$arguments = @('/nologo','/target:winexe',('/out:' + (Join-Path $PSScriptRoot $OutputName)))
$appIcon = Join-Path $PSScriptRoot 'assets/app.ico'
if (!(Test-Path -LiteralPath $appIcon)) { throw 'Missing application icon: assets/app.ico' }
$arguments += '/win32icon:' + $appIcon
$arguments += '/resource:' + $appIcon + ',PTVgame.AppIcon.ico'
foreach ($ref in $refs) { $arguments += '/reference:' + (Join-Path $framework $ref) }
$arguments += Join-Path $PSScriptRoot 'Launcher.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherShell.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherUpdate.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherLanguage.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherAccount.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherAccountUI.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherGameToEarn.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherGteOverlay.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherRunner.cs'
& (Join-Path $framework 'csc.exe') $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Build complete: $OutputName"

