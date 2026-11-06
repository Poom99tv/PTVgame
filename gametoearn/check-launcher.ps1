$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dist=Join-Path $root 'dist'
New-Item -ItemType Directory $dist -Force | Out-Null
$source=[IO.File]::ReadAllText((Join-Path $root 'LauncherGameToEarn.cs'))
$needle='[DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint n,INPUT[] inputs,int size);'
if(-not $source.Contains($needle)){throw 'Input test seam not found'}
$replacement='static uint SendInput(uint n,INPUT[] inputs,int size) { if((inputs[0].u.ki.dwFlags&2)==0)Interlocked.Increment(ref FeatureCheck.DownCount);return n; }'
$mock=Join-Path $dist 'LauncherGameToEarn.Mock.cs'
[IO.File]::WriteAllText($mock,$source.Replace($needle,$replacement))
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$args=@('/nologo','/target:exe','/main:FeatureCheck',('/out:'+(Join-Path $root 'PTVRunner-FeatureCheck.exe')))
foreach($ref in @('System.dll','System.Core.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','System.Xaml.dll','System.Security.dll')){$args+='/reference:'+(Join-Path $framework $ref)}
foreach($file in Get-ChildItem $root -Filter 'Launcher*.cs'){if($file.Name-ne 'LauncherGameToEarn.cs'){$args+=$file.FullName}}
$args+=$mock;$args+=Join-Path $PSScriptRoot 'FeatureCheck.cs'
& (Join-Path $framework 'csc.exe') $args
if($LASTEXITCODE-ne 0){throw 'Feature-check compile failed'}
& (Join-Path $root 'PTVRunner-FeatureCheck.exe')
if($LASTEXITCODE-ne 0){throw 'Feature-check failed'}
