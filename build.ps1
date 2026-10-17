param([string]$OutputName = 'PTVgame-Launcher.exe')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','System.Xaml.dll')
$arguments = @('/nologo','/target:winexe',('/out:' + (Join-Path $PSScriptRoot $OutputName)))
foreach ($ref in $refs) { $arguments += '/reference:' + (Join-Path $framework $ref) }
$arguments += Join-Path $PSScriptRoot 'Launcher.cs'
$arguments += Join-Path $PSScriptRoot 'LauncherShell.cs'
& (Join-Path $framework 'csc.exe') $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Build complete: $OutputName"
