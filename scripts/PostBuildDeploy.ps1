param(
    [Parameter(Mandatory = $true)][string] $TargetPath,
    [Parameter(Mandatory = $true)][string] $VaMPath,
    [Parameter(Mandatory = $true)][string] $ProjectDir
)

# Exit 0 always: copy/lock failures become MSBuild warnings (yellow), not build errors.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$script:WarningCount = 0

function Emit-Warning {
    param([string] $Subject, [string] $Code, [string] $Message)
    $script:WarningCount++
    Write-Host ("{0} : warning {1} : {2}" -f $Subject, $Code, $Message)
}

function Ensure-Dir {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        try {
            New-Item -ItemType Directory -Path $Path -Force | Out-Null
        } catch {
            Emit-Warning $Path 'PBD002' ("Could not create directory: " + $_.Exception.Message)
        }
    }
}

function Copy-FileWithRetry {
    param(
        [string] $SourcePath,
        [string] $DestDir
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        Emit-Warning $SourcePath 'PBD003' "Source file missing; skipping copy."
        return $false
    }

    Ensure-Dir $DestDir

    $srcDir = Split-Path -Parent $SourcePath
    $srcName = Split-Path -Leaf $SourcePath
    $destFull = Join-Path $DestDir $srcName

    # robocopy retries on a locked dest (SHARING_VIOLATION); /R:5 /W:2 = 5 tries, 2s apart.
    & robocopy $srcDir $DestDir $srcName /R:5 /W:2 /NJH /NJS /NDL /NFL /NC /NS /NP | Out-Null
    $rc = $LASTEXITCODE

    if ($rc -ge 8) {
        $hint = if ($rc -band 16) { " (destination likely locked - is VaM running?)" } else { "" }
        Emit-Warning $destFull 'PBD005' ("Robocopy failed (exit {0}){1}." -f $rc, $hint)
        return $false
    }

    # robocopy can exit success yet leave a stale dest; require dest mtime >= source.
    try {
        $srcMtime = (Get-Item -LiteralPath $SourcePath).LastWriteTimeUtc
        $dstItem = Get-Item -LiteralPath $destFull -ErrorAction Stop
        $dstMtime = $dstItem.LastWriteTimeUtc
        if ($dstMtime -lt $srcMtime.AddSeconds(-2)) {
            Emit-Warning $destFull 'PBD006' ("Destination mtime ({0:o}) older than source ({1:o}); file may not have been replaced." -f $dstMtime, $srcMtime)
            return $false
        }
    } catch {
        Emit-Warning $destFull 'PBD007' ("Could not verify destination after copy: " + $_.Exception.Message)
        return $false
    }

    return $true
}

function Copy-DirRecursive {
    param([string] $SourceDir, [string] $DestDir)

    if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) { return }
    Ensure-Dir $DestDir

    & robocopy $SourceDir $DestDir /E /NFL /NDL /NJH /NJS /NC /NS /NP /R:5 /W:2 | Out-Null
    $rc = $LASTEXITCODE

    if ($rc -ge 8) {
        Emit-Warning $DestDir 'PBD008' ("Robocopy dir failed (exit {0}): {1} -> {2}" -f $rc, $SourceDir, $DestDir)
    }
}

if (-not (Test-Path -LiteralPath $TargetPath)) {
    Emit-Warning $TargetPath 'PBD010' "Built DLL not found at TargetPath; aborting deploy."
    exit 0
}

$vamPathOk = (Test-Path -LiteralPath $VaMPath -PathType Container)
if (-not $vamPathOk) {
    Write-Host ("[PostBuildDeploy] VaMPath '{0}' not found - skipping VaM deploy, vam_patch staging will still run." -f $VaMPath)
}

$vamPlugins = Join-Path $VaMPath 'BepInEx\plugins'
$patchPlugins = Join-Path $ProjectDir 'vam_patch\BepInEx\plugins'

if ($vamPathOk) { [void](Copy-FileWithRetry $TargetPath $vamPlugins) }
[void](Copy-FileWithRetry $TargetPath $patchPlugins)

if ($vamPathOk) {
    $legacySqlite = Join-Path $VaMPath 'BepInEx\scripts\sqlite3.dll'
    if (Test-Path -LiteralPath $legacySqlite) {
        try {
            Remove-Item -LiteralPath $legacySqlite -Force -ErrorAction Stop
        } catch {
            Emit-Warning $legacySqlite 'PBD011' ("Could not remove legacy sqlite3: " + $_.Exception.Message)
        }
    }
}

$sqliteSrc = Join-Path $ProjectDir 'lib\sqlite-native\sqlite3.dll'
if (Test-Path -LiteralPath $sqliteSrc) {
    if ($vamPathOk) { [void](Copy-FileWithRetry $sqliteSrc $vamPlugins) }
    [void](Copy-FileWithRetry $sqliteSrc $patchPlugins)
}

$turboSrc = Join-Path $ProjectDir 'lib\turbojpeg\turbojpeg.dll'
$turboInPatch = Join-Path $patchPlugins 'turbojpeg.dll'
if (Test-Path -LiteralPath $turboSrc) {
    if ($vamPathOk) { [void](Copy-FileWithRetry $turboSrc $vamPlugins) }
    [void](Copy-FileWithRetry $turboSrc $patchPlugins)
} elseif ($vamPathOk -and (Test-Path -LiteralPath $turboInPatch)) {
    # Seed VaMPath from the staged copy only when absent; never overwrite a live turbojpeg.dll.
    $vamTurbo = Join-Path $vamPlugins 'turbojpeg.dll'
    if (-not (Test-Path -LiteralPath $vamTurbo)) {
        [void](Copy-FileWithRetry $turboInPatch $vamPlugins)
    }
}

$assetDirs = @('vpb_translations', 'vpb_fonts', 'vpb_icons', 'vpb_themes', 'vpb_help')
foreach ($name in $assetDirs) {
    if (-not $vamPathOk) { break }
    $srcDir = Join-Path $patchPlugins $name
    $dstDir = Join-Path $vamPlugins $name
    Copy-DirRecursive $srcDir $dstDir
}

if ($script:WarningCount -gt 0) {
    Write-Host ("[PostBuildDeploy] Completed with {0} warning(s); build successful. Review warnings above." -f $script:WarningCount)
    Write-Host ("[PostBuildDeploy] VaMPath deploy: {0}" -f $(if ($vamPathOk) { 'attempted (see warnings for any failures)' } else { 'skipped (VaMPath not found)' }))
} elseif ($vamPathOk) {
    Write-Host "[PostBuildDeploy] All targets deployed to VaMPath and vam_patch."
} else {
    Write-Host "[PostBuildDeploy] vam_patch staged; VaMPath deploy skipped (path not found)."
}

exit 0
