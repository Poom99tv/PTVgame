$ErrorActionPreference='Stop'
$stage=Join-Path $PSScriptRoot 'payload'
$dist=Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'
New-Item -ItemType Directory -Path $stage,$dist -Force|Out-Null
$nodeIndex=Invoke-RestMethod 'https://nodejs.org/dist/index.json'
$nodeRelease=$nodeIndex | Where-Object {$_.version -like 'v24.*' -and $_.lts} | Select-Object -First 1
if(-not $nodeRelease){throw 'Node 24 LTS unavailable'}
$nodeBase='https://nodejs.org/dist/'+$nodeRelease.version
Invoke-WebRequest ($nodeBase+'/win-x64/node.exe') -OutFile (Join-Path $stage 'node.exe')
$checks=(Invoke-WebRequest ($nodeBase+'/SHASUMS256.txt')).Content
$expected=([regex]::Match($checks,'(?m)^([a-f0-9]{64})\s+win-x64/node\.exe\s*$')).Groups[1].Value
if(-not $expected -or (Get-FileHash (Join-Path $stage 'node.exe') -Algorithm SHA256).Hash.ToLower() -ne $expected){throw 'Node checksum mismatch'}
Invoke-WebRequest 'https://github.com/cloudflare/cloudflared/releases/download/2026.10.3/cloudflared-windows-amd64.exe' -OutFile (Join-Path $stage 'cloudflared.exe')
if((Get-FileHash (Join-Path $stage 'cloudflared.exe') -Algorithm SHA256).Hash.ToLower() -ne '76b2805253d0d6ffacb092a7acfbee99e7af6917835c0c5410e23f3ef5052da2'){throw 'cloudflared checksum mismatch'}
foreach($name in @('server.mjs','pairing.mjs','events.mjs','parser.mjs','chat.mjs','gift-resolver.mjs','image-match.mjs','gift-catalog.json','package.json','package-lock.json')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage -Force}
Push-Location $stage
try{npm ci --omit=dev;if($LASTEXITCODE-ne 0){throw 'npm ci failed'}}finally{Pop-Location}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload=Join-Path $dist 'bridge-payload.zip'
if(Test-Path $payload){Remove-Item -LiteralPath $payload}
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$payload,[IO.Compression.CompressionLevel]::Optimal,$false)
$exe=Join-Path $dist 'TikTokBridge-v7.exe'
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$exe" "/resource:$payload,bridge.payload" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'TikTokBridge.cs')
if($LASTEXITCODE-ne 0){throw 'Bridge compile failed'}
$env:BRIDGE_TEST_ONLY='1'
try{$p=Start-Process $exe -ArgumentList @('--smoke-test',('"'+(Join-Path $dist 'bridge-smoke')+'"'),('"'+(Join-Path $dist 'bridge-smoke.json')+'"')) -WindowStyle Hidden -Wait -PassThru;if($p.ExitCode-ne 0){throw 'Bridge smoke test failed'}}finally{Remove-Item Env:BRIDGE_TEST_ONLY -ErrorAction SilentlyContinue}
Get-Content (Join-Path $dist 'bridge-smoke.json')

