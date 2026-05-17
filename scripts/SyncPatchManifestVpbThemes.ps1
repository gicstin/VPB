<#
.SYNOPSIS
  Rebuilds vpb_themes entries in vam_patch/patch_manifest.json from files on disk.

.DESCRIPTION
  Removes every manifest row whose RelativePath starts with BepInEx/plugins/vpb_themes,
  then inserts a directory row plus one row per file (sorted), immediately before the
  first BepInEx/plugins/zstd entry. Run after adding or removing files under vpb_themes.
#>
param(
    [string] $ProjectDir = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$manifestPath = Join-Path $ProjectDir 'vam_patch/patch_manifest.json'
$themesRoot = Join-Path $ProjectDir 'vam_patch/BepInEx/plugins/vpb_themes'
$prefix = 'BepInEx/plugins/vpb_themes'

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Manifest not found: $manifestPath"
}

$raw = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
$manifest = $raw | ConvertFrom-Json
if (-not ($manifest -is [System.Array])) {
    throw 'patch_manifest.json must be a JSON array.'
}

$themeRelPaths = @()
if (Test-Path -LiteralPath $themesRoot) {
    Get-ChildItem -LiteralPath $themesRoot -File -Recurse | ForEach-Object {
        $rel = ($_.FullName.Substring($themesRoot.Length).TrimStart('\', '/') -replace '\\', '/')
        if ($rel) {
            $themeRelPaths += "$prefix/$rel"
        }
    }
    $themeRelPaths = $themeRelPaths | Sort-Object
}

$themeBlock = [System.Collections.Generic.List[object]]::new()
$themeBlock.Add([pscustomobject]@{ RelativePath = $prefix; IsDirectory = $true })
foreach ($p in $themeRelPaths) {
    $themeBlock.Add([pscustomobject]@{ RelativePath = $p; IsDirectory = $false })
}

$out = [System.Collections.Generic.List[object]]::new()
$inserted = $false
foreach ($entry in $manifest) {
    $rp = [string]$entry.RelativePath
    if ($rp.StartsWith($prefix)) {
        continue
    }
    if (-not $inserted -and $rp -eq 'BepInEx/plugins/zstd') {
        foreach ($tc in $themeBlock) {
            $out.Add($tc)
        }
        $inserted = $true
    }
    $out.Add($entry)
}

if (-not $inserted) {
    foreach ($tc in $themeBlock) {
        $out.Add($tc)
    }
}

$json = $out | ConvertTo-Json -Depth 5
$newContent = $json + "`r`n"

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$currentContent = [System.IO.File]::ReadAllText($manifestPath, $utf8NoBom)

if ($currentContent -eq $newContent) {
    Write-Host "No changes needed for $manifestPath ($($themeBlock.Count) vpb_themes rows, $($themeRelPaths.Count) files)."
} else {
    [System.IO.File]::WriteAllText($manifestPath, $newContent, $utf8NoBom)
    Write-Host "Updated $manifestPath ($($themeBlock.Count) vpb_themes rows, $($themeRelPaths.Count) files)."
}
