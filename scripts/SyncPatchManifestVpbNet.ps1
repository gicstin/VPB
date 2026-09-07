param([string] $ProjectDir = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$patchRoot = Join-Path $ProjectDir 'vam_patch'
$manifestPath = Join-Path $patchRoot 'patch_manifest.json'
$netPath = Join-Path $patchRoot 'BepInEx/plugins/VPB/net'
$prefix = 'BepInEx/plugins/VPB/net'
$entries = @(Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json)
$result = @($entries | Where-Object { $_.RelativePath -ne $prefix -and -not $_.RelativePath.StartsWith($prefix + '/') })
foreach ($name in @('VpbNet.exe', 'steam_api64.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $netPath $name))) { throw "Missing multiplayer payload: $name" }
    $result += [pscustomobject]@{ RelativePath = $prefix + '/' + $name; IsDirectory = $false }
}
$json = $result | ConvertTo-Json -Depth 8
if ($json.Trim() -ne ([IO.File]::ReadAllText($manifestPath)).Trim()) {
    [IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
