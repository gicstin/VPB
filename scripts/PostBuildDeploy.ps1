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

$vamPluginsRoot = Join-Path $VaMPath 'BepInEx\plugins'
$vamPlugins = Join-Path $vamPluginsRoot 'VPB'
$patchPlugins = Join-Path $ProjectDir 'vam_patch\BepInEx\plugins\VPB'

if ($vamPathOk) { [void](Copy-FileWithRetry $TargetPath $vamPlugins) }
[void](Copy-FileWithRetry $TargetPath $patchPlugins)

if ($vamPathOk) {
    $legacyFiles = @('VPB.dll', 'VPB.pdb', 'sqlite3.dll', 'turbojpeg.dll', 'vpb_icons.pack', 'VPB_THIRD_PARTY_NOTICES.txt', 'bench_run.cfg', 'bench_run.example.cfg')
    $legacyDirs = @('vpb_fonts', 'vpb_help', 'vpb_translations', 'vpb_themes', 'vpb_ccm_clips', 'vpb_icons', 'VpbNet', 'zstd', 'bench', 'vpb_update_staging')
    foreach ($name in $legacyFiles) {
        $p = Join-Path $vamPluginsRoot $name
        if (Test-Path -LiteralPath $p -PathType Leaf) {
            try {
                Remove-Item -LiteralPath $p -Force -ErrorAction Stop
                Write-Host "[PostBuildDeploy] Removed legacy $p"
            } catch {
                Emit-Warning $p 'PBD012' ("Could not remove legacy file: " + $_.Exception.Message)
            }
        }
    }
    foreach ($name in $legacyDirs) {
        $p = Join-Path $vamPluginsRoot $name
        if (Test-Path -LiteralPath $p -PathType Container) {
            try {
                Remove-Item -LiteralPath $p -Recurse -Force -Confirm:$false -ErrorAction Stop
                Write-Host "[PostBuildDeploy] Removed legacy $p"
            } catch {
                Emit-Warning $p 'PBD012' ("Could not remove legacy directory: " + $_.Exception.Message)
            }
        }
    }
}

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

$patchNative = Join-Path $patchPlugins 'native'
$vamNative = Join-Path $vamPlugins 'native'

$sqliteSrc = Join-Path $ProjectDir 'lib\sqlite-native\sqlite3.dll'
if (Test-Path -LiteralPath $sqliteSrc) {
    if ($vamPathOk) { [void](Copy-FileWithRetry $sqliteSrc $vamNative) }
    [void](Copy-FileWithRetry $sqliteSrc $patchNative)
}

$turboSrc = Join-Path $ProjectDir 'lib\turbojpeg\turbojpeg.dll'
$turboInPatch = Join-Path $patchNative 'turbojpeg.dll'
if (Test-Path -LiteralPath $turboSrc) {
    if ($vamPathOk) { [void](Copy-FileWithRetry $turboSrc $vamNative) }
    [void](Copy-FileWithRetry $turboSrc $patchNative)
} elseif ($vamPathOk -and (Test-Path -LiteralPath $turboInPatch)) {
    # Seed VaMPath from the staged copy only when absent; never overwrite a live turbojpeg.dll.
    $vamTurbo = Join-Path $vamNative 'turbojpeg.dll'
    if (-not (Test-Path -LiteralPath $vamTurbo)) {
        [void](Copy-FileWithRetry $turboInPatch $vamNative)
    }
}

# The patcher prunes anything under BepInEx/plugins/VPB that this manifest does not list, so the
# install carries its own copy - that is what makes the next relocation self-cleaning.
$manifestSrc = Join-Path $ProjectDir 'vam_patch\patch_manifest.json'
if (Test-Path -LiteralPath $manifestSrc) {
    [void](Copy-FileWithRetry $manifestSrc $patchPlugins)
    if ($vamPathOk) { [void](Copy-FileWithRetry $manifestSrc $vamPlugins) }
} else {
    Emit-Warning $manifestSrc 'PBD013' "patch_manifest.json missing; the shipped copy was not refreshed and the prune will stay inert."
}

$patcherSrc = Join-Path $ProjectDir 'src\patcher\VPBPatcher.cs'
$sharedSrc = Join-Path $ProjectDir 'src\util\VpbLegacyLayout.cs'
$patcherDll = Join-Path $ProjectDir 'vam_patch\BepInEx\patchers\VPB.Patcher.dll'
if (-not (Test-Path -LiteralPath $patcherDll)) {
    Emit-Warning $patcherDll 'PBD014' "Shipped VPB.Patcher.dll missing; users get no legacy-layout cleanup. Build VPBPatcher.csproj."
} else {
    $dllMtime = (Get-Item -LiteralPath $patcherDll).LastWriteTimeUtc
    foreach ($srcPath in @($patcherSrc, $sharedSrc)) {
        if (-not (Test-Path -LiteralPath $srcPath)) { continue }
        $srcMtime = (Get-Item -LiteralPath $srcPath).LastWriteTimeUtc
        if ($dllMtime -lt $srcMtime) {
            Emit-Warning $patcherDll 'PBD014' ("Shipped VPB.Patcher.dll ({0:o}) is older than {1} ({2:o}); rebuild VPBPatcher.csproj and commit the DLL or users keep running the old patcher." -f $dllMtime, (Split-Path -Leaf $srcPath), $srcMtime)
        }
    }
}

# net/ carries the multiplayer broker plus the steam_api64.dll it loads at runtime; the build
# republishes it into vam_patch before compiling, so this pushes the fresh one into VaM.
$assetDirs = @('assets', 'native', 'net')
foreach ($name in $assetDirs) {
    if (-not $vamPathOk) { break }
    $srcDir = Join-Path $patchPlugins $name
    $dstDir = Join-Path $vamPlugins $name
    Copy-DirRecursive $srcDir $dstDir
}

$assetFiles = @('VPB_THIRD_PARTY_NOTICES.txt')
foreach ($name in $assetFiles) {
    if (-not $vamPathOk) { break }
    $srcFile = Join-Path $patchPlugins $name
    if (Test-Path -LiteralPath $srcFile) {
        [void](Copy-FileWithRetry $srcFile $vamPlugins)
    }
}

# Anything left over inside BepInEx/plugins/VPB from an older layout is removed by VPB.Patcher at
# the next VaM launch, driven by the manifest copied above - nothing to name here.

if ($script:WarningCount -gt 0) {
    Write-Host ("[PostBuildDeploy] Completed with {0} warning(s); build successful. Review warnings above." -f $script:WarningCount)
    Write-Host ("[PostBuildDeploy] VaMPath deploy: {0}" -f $(if ($vamPathOk) { 'attempted (see warnings for any failures)' } else { 'skipped (VaMPath not found)' }))
} elseif ($vamPathOk) {
    Write-Host "[PostBuildDeploy] All targets deployed to VaMPath and vam_patch."
} else {
    Write-Host "[PostBuildDeploy] vam_patch staged; VaMPath deploy skipped (path not found)."
}

exit 0
