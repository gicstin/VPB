param([string] $ProjectDir = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$project = Join-Path $ProjectDir 'tools/VpbNet/VpbNet.csproj'
$output = Join-Path $ProjectDir 'vam_patch/BepInEx/plugins/VPB/net'
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw "Broker publish failed: $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath (Join-Path $output 'VpbNet.exe'))) { throw 'Broker publish produced no executable.' }
