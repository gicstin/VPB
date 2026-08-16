<#
.SYNOPSIS
  Rebuilds the icon rows in vam_patch/patch_manifest.json.

.DESCRIPTION
  Icons ship as a single packed atlas (BepInEx/plugins/vpb_icons.pack) built from
  assets/vpb_icons by scripts/build_icon_atlas.py, plus the third-party notice file
  the Tabler Icons MIT licence requires us to redistribute.

  This removes every legacy per-PNG row under BepInEx/plugins/vpb_icons and any prior
  pack/notice row, then inserts the current rows immediately before the first
  BepInEx/plugins/zstd entry. Run after rebuilding the pack.
#>
param(
    [string] $ProjectDir = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$manifestPath = Join-Path $ProjectDir 'vam_patch/patch_manifest.json'
$patchRoot = Join-Path $ProjectDir 'vam_patch'
$legacyPrefix = 'BepInEx/plugins/vpb_icons'
$shipped = @(
    'BepInEx/plugins/vpb_icons.pack',
    'BepInEx/plugins/VPB_THIRD_PARTY_NOTICES.txt'
)

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Manifest not found: $manifestPath"
}

$raw = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
$manifest = $raw | ConvertFrom-Json
if (-not ($manifest -is [System.Array])) {
    throw 'patch_manifest.json must be a JSON array.'
}

$iconBlock = [System.Collections.Generic.List[object]]::new()
foreach ($rel in $shipped) {
    $full = Join-Path $patchRoot $rel
    if (-not (Test-Path -LiteralPath $full)) {
        throw "Shipped icon file missing: $full (run scripts/build_icon_atlas.py)"
    }
    $iconBlock.Add([pscustomobject]@{ RelativePath = $rel; IsDirectory = $false })
}

$out = [System.Collections.Generic.List[object]]::new()
$inserted = $false
foreach ($entry in $manifest) {
    $rp = [string]$entry.RelativePath
    if ($rp.StartsWith($legacyPrefix) -or $shipped -contains $rp) {
        continue
    }
    if (-not $inserted -and $rp -eq 'BepInEx/plugins/zstd') {
        foreach ($ic in $iconBlock) {
            $out.Add($ic)
        }
        $inserted = $true
    }
    $out.Add($entry)
}

if (-not $inserted) {
    foreach ($ic in $iconBlock) {
        $out.Add($ic)
    }
}

$json = $out | ConvertTo-Json -Depth 5
$newContent = $json + "`r`n"

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
$currentContent = [System.IO.File]::ReadAllText($manifestPath, $utf8NoBom)

if ($currentContent -eq $newContent) {
    Write-Host "No changes needed for $manifestPath ($($iconBlock.Count) icon rows)."
} else {
    [System.IO.File]::WriteAllText($manifestPath, $newContent, $utf8NoBom)
    Write-Host "Updated $manifestPath ($($iconBlock.Count) icon rows)."
}
