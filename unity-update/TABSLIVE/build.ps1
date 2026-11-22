param([string]$GameDir='C:\Users\Windows 11 Pro\Downloads\New folder (6)\Totally Accurate Battle Simulator')
$ErrorActionPreference='Stop'
$managed=Join-Path $GameDir 'TotallyAccurateBattleSimulator_Data\Managed'
$core=Join-Path $GameDir 'BepInEx\core'
$compiler='C:\Program Files\Unity\Hub\Editor\6000.0.59f2\Editor\Data\DotNetSdkRoslyn\csc.dll'
$runtime='C:\Program Files\Unity\Hub\Editor\6000.0.59f2\Editor\Data\NetCoreRuntime\dotnet.exe'
$arguments=@('-nologo','-target:library','-nostdlib+','-langversion:latest','-optimize+',('-out:'+(Join-Path $PSScriptRoot 'TabsTikTokArena.dll')))
$arguments+=Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {'-r:'+$_.FullName}
$arguments+=('-r:'+(Join-Path $core 'BepInEx.dll'))
$arguments+=('-r:'+(Join-Path $core '0Harmony.dll'))
$arguments+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
$arguments+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Locales') -Filter '*.json' | ForEach-Object {'-resource:'+$_.FullName+',TabsLocales.'+$_.Name}
& $runtime $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Mod compilation failed'}
