<#
.SYNOPSIS
  Generates vam_patch/patch_manifest2.json from the shipped file list in patch_manifest.json.

.DESCRIPTION
  patch_manifest.json stays byte-compatible forever: every VPB install already in the field
  parses it, and those installs can only be fixed by an update they fetch through it. The
  hashed manifest therefore ships under a new name and the old one is never reshaped.

  v2 carries the git blob SHA1 and size of every shipped file plus the version that was
  actually built, which lets the updater diff and verify from one request instead of
  plugin_version.txt + patch_manifest.json + the rate-limited GitHub tree API.

  The version comes from obj/VPB_built_version.txt written by PreparePluginVersion.ps1.
  plugin_version.txt is bumped to n+1 during the build, so it never names this artefact.
#>
param(
    [string] $ProjectDir = (Split-Path -Parent $PSScriptRoot),
    [string] $Version = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$patchRoot = Join-Path $ProjectDir 'vam_patch'
$v1Path = Join-Path $patchRoot 'patch_manifest.json'
$v2Path = Join-Path $patchRoot 'patch_manifest2.json'
$stampPath = Join-Path $ProjectDir 'obj/VPB_built_version.txt'
$dbPath = Join-Path $ProjectDir 'src/gallery/index/VpbLocalDatabase.cs'

# Hashing the bytes on disk is WRONG here. core.autocrlf leaves text files CRLF in the working
# tree while git stores - and raw.githubusercontent serves - the LF form. A client hashes what it
# downloaded, so a disk-byte hash never matches and the updater aborts on a checksum mismatch that
# is not real. git hash-object applies the same filters git will, so the manifest describes the
# bytes the client actually receives, whatever autocrlf is set to on the machine that built it.
function Get-GitBlobInfo([string[]] $paths) {
    $result = @{}
    if ($paths.Count -eq 0) { return $result }

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $shas = @(& git -C $ProjectDir hash-object -w -- @paths 2>&1 |
            Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] })
        if ($LASTEXITCODE -ne 0 -or $shas.Count -ne $paths.Count) {
            throw "git hash-object failed ($($shas.Count) hashes for $($paths.Count) files). Is git on PATH?"
        }

        $sizes = New-Object string[] $shas.Count
        for ($i = 0; $i -lt $shas.Count; $i++) {
            $sizes[$i] = ([string](& git -C $ProjectDir cat-file -s ([string]$shas[$i]).Trim() 2>&1 |
                Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] })).Trim()
            if ($LASTEXITCODE -ne 0) { throw "git cat-file failed while sizing blob $($shas[$i])." }
        }

        for ($i = 0; $i -lt $paths.Count; $i++) {
            $result[$paths[$i]] = [pscustomobject]@{
                Sha1 = ([string]$shas[$i]).Trim()
                Size = [int64]([string]$sizes[$i]).Trim()
            }
        }
    }
    finally { $ErrorActionPreference = $prev }

    return $result
}

function Get-BuiltVersion() {
    if ($Version -ne '') { return $Version.Trim() }
    if (Test-Path -LiteralPath $stampPath) {
        $stamped = ([System.IO.File]::ReadAllText($stampPath)).Trim()
        if ($stamped -ne '') { return $stamped }
    }
    $versionFile = Join-Path $ProjectDir 'plugin_version.txt'
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw "No built-version stamp at $stampPath and no plugin_version.txt to fall back on."
    }
    $lines = @(
        (Get-Content -LiteralPath $versionFile) | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
    )
    if ($lines.Count -lt 2 -or $lines[1] -notmatch '^\d+$') {
        throw "plugin_version.txt is malformed; cannot derive a built version."
    }
    $base = ($lines[0] -replace '^[vV]', '').Trim()
    $n = [int]$lines[1] - 1
    if ($n -lt 0) { $n = 0 }
    Write-Warning "[BuildPatchManifest] No build stamp; derived $base.$n from plugin_version.txt (n-1). Rebuild to get an exact version."
    return "$base.$n"
}

function Get-SchemaVersion() {
    if (-not (Test-Path -LiteralPath $dbPath)) { return 0 }
    $text = Get-Content -LiteralPath $dbPath -Raw
    $m = [regex]::Match($text, 'const\s+int\s+SchemaVersion\s*=\s*(\d+)\s*;')
    if (-not $m.Success) { return 0 }
    return [int]$m.Groups[1].Value
}

if (-not (Test-Path -LiteralPath $v1Path)) {
    throw "Manifest not found: $v1Path"
}

$v1 = (Get-Content -LiteralPath $v1Path -Raw -Encoding UTF8) | ConvertFrom-Json
if (-not ($v1 -is [System.Array])) {
    throw 'patch_manifest.json must be a JSON array.'
}

$missing = [System.Collections.Generic.List[string]]::new()
$hashPaths = [System.Collections.Generic.List[string]]::new()

foreach ($entry in $v1) {
    if ([bool]$entry.IsDirectory) { continue }
    $rel = [string]$entry.RelativePath
    $full = Join-Path $patchRoot $rel
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { $missing.Add($rel); continue }
    $hashPaths.Add("vam_patch/$rel")
}

if ($missing.Count -gt 0) {
    throw ("patch_manifest.json lists file(s) that are not staged in vam_patch:`n  " + ($missing -join "`n  "))
}

$blobs = Get-GitBlobInfo $hashPaths.ToArray()

$files = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $v1) {
    $rel = [string]$entry.RelativePath

    if ([bool]$entry.IsDirectory) {
        $files.Add([pscustomobject]@{
            RelativePath = $rel
            IsDirectory  = $true
            Sha1         = ''
            Size         = 0
        })
        continue
    }

    $info = $blobs["vam_patch/$rel"]
    if ($null -eq $info) { throw "No blob hash produced for $rel." }

    $files.Add([pscustomobject]@{
        RelativePath = $rel
        IsDirectory  = $false
        Sha1         = $info.Sha1
        Size         = $info.Size
    })
}

$manifest = [pscustomobject]@{
    ManifestVersion = 2
    Version         = Get-BuiltVersion
    BuiltUtc        = [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    Schema          = Get-SchemaVersion
    Files           = $files.ToArray()
}

$json = $manifest | ConvertTo-Json -Depth 5
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($v2Path, $json.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n", $utf8NoBom)

$fileCount = @($files | Where-Object { -not $_.IsDirectory }).Count
Write-Host ("[BuildPatchManifest] patch_manifest2.json: v{0}, {1} file(s), schema {2}." -f $manifest.Version, $fileCount, $manifest.Schema)
