<#
.SYNOPSIS
  Maintains releases/index.json - the list of shipped VPB builds a user can roll back to.

.DESCRIPTION
  Only commits that actually change the shipped VPB.dll are builds; the other ~900 commits in
  this repo carry no binary, so browsing to one and taking its zip hands you whatever build was
  committed before it. This index names the real ones.

  Fetched by the updater over raw.githubusercontent at a fixed ref, so enumerating versions
  costs no GitHub API call and is not subject to the 60 req/hr unauthenticated limit.

  -Backfill rebuilds the whole index from history. Without it the current build is added or
  refreshed in place. Tags are only ever created locally, never pushed.

  Tag names are flat (build-0.32.610). A slash would land in the GitHub tree API path as
  git/trees/build/0.32.610, which is not an endpoint: the call 404s, the updater gets no
  SHAs, and it refetches every shipped file without verifying any of them.

  0.32.406 (commit 86132935) moved the shipped tree to the single BepInEx/plugins/VPB folder.
  The patcher's prune and the VpbLegacyLayout sweep only migrate forward, so rolling an install
  back across that boundary is not supported: builds below MinRollbackVersion are kept out of
  the index and never tagged, which leaves no ref for a client to point at.

  -Keep bounds the index. Every client fetches it, and at this build cadence an unbounded list
  reaches four figures within a year. Tags follow the index: any build-* tag naming a release the
  index no longer lists is deleted, so retention is one knob rather than two.
#>
param(
    [string] $ProjectDir = (Split-Path -Parent $PSScriptRoot),
    [switch] $Backfill,
    [string] $Version = '',
    [string] $Notes = '',
    [switch] $CreateTags,
    [int] $TagLimit = 40,
    [string] $TagSince = '',
    [string] $MinRollbackVersion = '0.32.406',
    [string] $MinRollbackCommit = '86132935',
    [int] $Keep = 60,
    [string] $ReleaseBranch = 'main',
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dllPaths = @(
    'vam_patch/BepInEx/plugins/VPB/VPB.dll',
    'vam_patch/BepInEx/plugins/VPB.dll'
)
$indexDir = Join-Path $ProjectDir 'releases'
$indexPath = Join-Path $indexDir 'index.json'
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Invoke-Git([string[]] $gitArgs) {
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = @(& git -C $ProjectDir @gitArgs 2>&1 |
            Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] })
        if ($LASTEXITCODE -ne 0) { return $null }
        return $out
    }
    finally { $ErrorActionPreference = $prev }
}

function Get-VersionAtCommit([string] $sha) {
    $text = Invoke-Git @('show', "${sha}:plugin_version.txt")
    if ($null -eq $text) { return $null }
    $lines = @($text | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
    if ($lines.Count -lt 2 -or $lines[1] -notmatch '^\d+$') { return $null }
    $base = ($lines[0] -replace '^[vV]', '').Trim()
    $n = [int]$lines[1] - 1
    if ($n -lt 0) { return $null }
    return "$base.$n"
}

function Get-SchemaAtCommit([string] $sha) {
    foreach ($p in @('src/gallery/index/VpbLocalDatabase.cs', 'src/VpbLocalDatabase.cs')) {
        $text = Invoke-Git @('show', "${sha}:$p")
        if ($null -eq $text) { continue }
        $m = [regex]::Match(($text -join "`n"), 'const\s+int\s+SchemaVersion\s*=\s*(\d+)\s*;')
        if ($m.Success) { return [int]$m.Groups[1].Value }
    }
    return 0
}

function New-Entry([string] $sha, [string] $version, [string] $dateUtc, [string] $subject, [int] $schema) {
    return [pscustomobject]@{
        Version = $version
        Tag     = "build-$version"
        Commit  = $sha
        DateUtc = $dateUtc
        Notes   = $subject
        Schema  = $schema
    }
}

function Test-AboveFloor([string] $version, [string] $commit) {
    # The floor is a point in HISTORY (the single-folder layout change), not a number. Version
    # numbers are not monotonic here: a branch can be versioned into the future and merged back,
    # and changing the base in plugin_version.txt resets the build counter to 1 - so 0.33.x work
    # landing back on 0.32 would produce 0.32.1 and a version compare would wrongly bury it.
    # Ancestry answers the question that actually matters: does this build have the new layout?
    if ($commit -ne '' -and $MinRollbackCommit -ne '') {
        $null = Invoke-Git @('merge-base', '--is-ancestor', $MinRollbackCommit, $commit)
        if ($LASTEXITCODE -eq 0) { return $true }
        if ($LASTEXITCODE -eq 1) { return $false }
    }

    if ($MinRollbackVersion -eq '') { return $true }
    try { return ([version]$version) -ge ([version]$MinRollbackVersion) }
    catch { return $false }
}

function Write-Index($entries, [int] $excluded) {
    if (-not (Test-Path -LiteralPath $indexDir)) {
        New-Item -ItemType Directory -Path $indexDir -Force | Out-Null
    }
    $doc = [pscustomobject]@{
        IndexVersion       = 1
        UpdatedUtc         = [System.DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        MinRollbackVersion = $MinRollbackVersion
        ExcludedBelowMin   = $excluded
        Keep               = $Keep
        Releases           = @($entries)
    }
    $json = $doc | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($indexPath, $json.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n", $utf8NoBom)
}

function Read-Index() {
    if (-not (Test-Path -LiteralPath $indexPath)) { return @() }
    $doc = (Get-Content -LiteralPath $indexPath -Raw -Encoding UTF8) | ConvertFrom-Json
    if ($null -eq $doc.Releases) { return @() }
    return @($doc.Releases)
}

function Sort-Entries($entries) {
    return @($entries | Sort-Object -Property @{ Expression = { [datetime]$_.DateUtc } } -Descending)
}

$result = [System.Collections.Generic.List[object]]::new()
$script:BelowFloor = 0

$currentBranch = ([string](Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD'))).Trim()
$onReleaseBranch = ($currentBranch -eq '' -or $currentBranch -eq $ReleaseBranch)

if (-not $onReleaseBranch) {
    Write-Host ("[PublishRelease] On '{0}'. Every branch publishes its own releases/index.json and tags its own" -f $currentBranch)
    Write-Host ("[PublishRelease] builds, so users following this branch can roll back within it. Tags for other")
    Write-Host ("[PublishRelease] branches are never touched.")
}

if ($Backfill) {
    $log = Invoke-Git (@('log', '--format=%H%x1f%cI%x1f%s', '--') + $dllPaths)
    if ($null -eq $log) { throw 'git log failed; is this a git checkout?' }

    $seen = @{}
    $skipped = [System.Collections.Generic.List[string]]::new()

    foreach ($line in $log) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split ([char]0x1f)
        if ($parts.Count -lt 3) { continue }
        $sha = $parts[0]
        $date = ([datetime]$parts[1]).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        $subject = $parts[2]

        $v = Get-VersionAtCommit $sha
        if ($null -eq $v) {
            $skipped.Add("$($sha.Substring(0,8))  $subject")
            continue
        }
        if ($seen.ContainsKey($v)) { continue }
        $seen[$v] = $true
        if (-not (Test-AboveFloor $v $sha)) { $script:BelowFloor++; continue }
        $result.Add((New-Entry $sha $v $date $subject (Get-SchemaAtCommit $sha)))
    }

    Write-Host ("[PublishRelease] Backfilled {0} build(s) from history." -f $result.Count)
    Write-Host ("[PublishRelease] Excluded {0} build(s) below {1} (pre single-folder layout; rollback across it is unsupported)." -f $script:BelowFloor, $MinRollbackVersion)
    if ($skipped.Count -gt 0) {
        Write-Host ("[PublishRelease] Skipped {0} binary commit(s) from before plugin_version.txt existed." -f $skipped.Count)
    }
}
else {
    $sha = (Invoke-Git @('rev-parse', 'HEAD'))
    if ($null -eq $sha) { throw 'git rev-parse HEAD failed; is this a git checkout?' }
    $sha = ([string]$sha).Trim()

    $dirty = @(Invoke-Git (@('status', '--porcelain', '--') + $dllPaths) | Where-Object { "$_".Trim() -ne '' })
    if ($dirty.Count -gt 0 -and -not $Force) {
        throw ("The shipped VPB.dll is not committed, so a release recorded against HEAD ($($sha.Substring(0,8))) would " +
               "point at a build that is not in history. Commit the binary first, or pass -Force if you know better.")
    }

    $v = $Version.Trim()
    if ($v -eq '') {
        $stamp = Join-Path $ProjectDir 'obj/VPB_built_version.txt'
        if (Test-Path -LiteralPath $stamp) { $v = ([System.IO.File]::ReadAllText($stamp)).Trim() }
    }
    if ($v -eq '') {
        $m2 = Join-Path $ProjectDir 'vam_patch/patch_manifest2.json'
        if (Test-Path -LiteralPath $m2) {
            $v = ((Get-Content -LiteralPath $m2 -Raw -Encoding UTF8) | ConvertFrom-Json).Version
        }
    }
    if ([string]::IsNullOrWhiteSpace($v)) {
        throw 'No version given and none found in obj/VPB_built_version.txt or vam_patch/patch_manifest2.json.'
    }

    $subject = $Notes.Trim()
    if ($subject -eq '') { $subject = ([string](Invoke-Git @('log', '-1', '--format=%s'))).Trim() }
    $date = ([datetime](([string](Invoke-Git @('log', '-1', '--format=%cI'))).Trim())).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

    $schemaPath = Join-Path $ProjectDir 'src/gallery/index/VpbLocalDatabase.cs'
    $schema = 0
    if (Test-Path -LiteralPath $schemaPath) {
        $m = [regex]::Match((Get-Content -LiteralPath $schemaPath -Raw), 'const\s+int\s+SchemaVersion\s*=\s*(\d+)\s*;')
        if ($m.Success) { $schema = [int]$m.Groups[1].Value }
    }

    if (-not (Test-AboveFloor $v $sha)) {
        throw "Version $v is below MinRollbackVersion $MinRollbackVersion; it predates the single-folder plugin layout and must not be offered as a rollback target."
    }

    foreach ($e in (Read-Index)) {
        if ([string]$e.Version -eq $v) {
            $existingCommit = [string]$e.Commit
            if ($existingCommit -ne $sha) {
                throw ("Version $v is already recorded at commit $($existingCommit.Substring(0,8)) but HEAD is $($sha.Substring(0,8)).`n" +
                       "The build counter in plugin_version.txt is per working copy, so two people building from the same`n" +
                       "base both produce $v. Two different binaries cannot share one version: the tag would point at one`n" +
                       "and the index describe the other. Bump the base version in plugin_version.txt, rebuild, and retry.")
            }
            continue
        }
        if (-not (Test-AboveFloor ([string]$e.Version) ([string]$e.Commit))) { $script:BelowFloor++; continue }
        $result.Add($e)
    }
    $result.Add((New-Entry $sha $v $date $subject $schema))
    Write-Host ("[PublishRelease] Recorded {0} at {1}." -f $v, $sha.Substring(0, 8))
}

$sorted = @(Sort-Entries $result)

$retired = 0
if ($Keep -gt 0 -and $sorted.Count -gt $Keep) {
    $retired = $sorted.Count - $Keep
    $sorted = @($sorted | Select-Object -First $Keep)
    Write-Host ("[PublishRelease] Retention: kept the {0} most recent release(s), retired {1}." -f $Keep, $retired)
}

Write-Index $sorted $script:BelowFloor

$null = Invoke-Git @('fetch', '--tags', '--quiet')

$existingTags = @{}
$tagList = Invoke-Git @('tag', '--list', 'build-*')
if ($null -ne $tagList) { foreach ($t in $tagList) { $existingTags[[string]$t] = $true } }

$mismatched = [System.Collections.Generic.List[string]]::new()
$repointable = [System.Collections.Generic.List[string]]::new()

foreach ($e in $sorted) {
    $tag = [string]$e.Tag
    if (-not $existingTags.ContainsKey($tag)) { continue }
    $target = Invoke-Git @('rev-list', '-n', '1', $tag)
    if ($null -eq $target) { continue }
    $target = ([string]$target).Trim()
    if ($target -eq [string]$e.Commit) { continue }

    $containing = @(Invoke-Git @('branch', '-a', '--contains', $target) | Where-Object { "$_".Trim() -ne '' })
    if ($containing.Count -eq 0) {
        $repointable.Add([string]$tag)
        continue
    }

    $mismatched.Add(("{0} -> {1} but this branch's index names {2}" -f $tag, $target.Substring(0, 8), ([string]$e.Commit).Substring(0, 8)))
}

if ($mismatched.Count -gt 0) {
    Write-Host "[PublishRelease] TAG/INDEX MISMATCH - the updater resolves by tag, so users would get a different build"
    Write-Host "[PublishRelease] than the picker describes:"
    foreach ($m in $mismatched) { Write-Host "    $m" }
    Write-Host "[PublishRelease] Two live builds are claiming one version number - usually two branches or two people"
    Write-Host "[PublishRelease] whose plugin_version.txt counters advanced independently. Bump the base version in"
    Write-Host "[PublishRelease] plugin_version.txt on this branch, rebuild, and re-run."
    exit 1
}

if ($repointable.Count -gt 0) {
    if ($CreateTags) {
        foreach ($t in $repointable) {
            $null = Invoke-Git @('tag', '-d', [string]$t)
            $existingTags.Remove([string]$t)
            Write-Host ("[PublishRelease] Re-pointing {0}; its old commit was reset or rebased away." -f $t)
        }
    }
    else {
        Write-Host ("[PublishRelease] {0} tag(s) still point at commits that were reset or rebased away." -f $repointable.Count)
        Write-Host ("[PublishRelease] -CreateTags re-points them at the commits this index names:")
        foreach ($t in ($repointable | Select-Object -First 5)) { Write-Host "    $t" }
    }
}

$candidates = @($sorted)
if ($TagSince -ne '') {
    $cutoff = [datetime]::Parse($TagSince).ToUniversalTime()
    $candidates = @($candidates | Where-Object { ([datetime]$_.DateUtc).ToUniversalTime() -ge $cutoff })
}
if ($TagLimit -gt 0 -and $candidates.Count -gt $TagLimit) {
    $candidates = @($candidates | Select-Object -First $TagLimit)
}

# The index is what users read, and it promises every listed build is reachable. A tag that exists
# only locally breaks that promise silently: the picker offers the build, then the fetch 404s.
$remoteTags = @{}
$lsRemote = Invoke-Git @('ls-remote', '--tags', 'origin', 'build-*')
if ($null -ne $lsRemote) {
    foreach ($line in $lsRemote) {
        $name = ([string]$line -split "`t")[-1]
        $name = $name -replace '^refs/tags/', '' -replace '\^\{\}$', ''
        if ($name -ne '') { $remoteTags[$name] = $true }
    }
}

$unpublished = @($sorted | Where-Object { -not $remoteTags.ContainsKey([string]$_.Tag) })
if ($unpublished.Count -gt 0) {
    Write-Host ("[PublishRelease] WARNING: {0} of {1} listed release(s) have no tag on the remote." -f $unpublished.Count, $sorted.Count)
    Write-Host ("[PublishRelease] Users can see these in the version picker but cannot roll back to them - the fetch 404s.")
    foreach ($e in ($unpublished | Select-Object -First 5)) { Write-Host ("    {0}" -f $e.Tag) }
    if ($unpublished.Count -gt 5) { Write-Host ("    ... and {0} more" -f ($unpublished.Count - 5)) }
    Write-Host "[PublishRelease] Publish them with:"
    Write-Host "    git push origin --tags"
}

$eligible = @{}
foreach ($e in $sorted) { $eligible[[string]$e.Tag] = $true }

$staleTags = [System.Collections.Generic.List[string]]::new()
$orphanTags = [System.Collections.Generic.List[string]]::new()
$otherBranchTags = 0
foreach ($t in ($existingTags.Keys | Sort-Object)) {
    if ($eligible.ContainsKey([string]$t)) { continue }
    $target = Invoke-Git @('rev-list', '-n', '1', [string]$t)
    if ($null -eq $target) { continue }
    $target = ([string]$target).Trim()

    $null = Invoke-Git @('merge-base', '--is-ancestor', $target, 'HEAD')
    if ($LASTEXITCODE -eq 0) { $staleTags.Add([string]$t); continue }

    $containing = @(Invoke-Git @('branch', '-a', '--contains', $target) | Where-Object { "$_".Trim() -ne '' })
    if ($containing.Count -eq 0) { $orphanTags.Add([string]$t) } else { $otherBranchTags++ }
}
$staleTags = @($staleTags)
$orphanTags = @($orphanTags)

if ($otherBranchTags -gt 0) {
    Write-Host ("[PublishRelease] {0} tag(s) belong to other branches and were left untouched." -f $otherBranchTags)
}

if ($orphanTags.Count -gt 0) {
    if ($CreateTags) {
        foreach ($t in $orphanTags) {
            $null = Invoke-Git @('tag', '-d', [string]$t)
            Write-Host ("[PublishRelease] Removed orphaned tag {0} (its commit is on no branch - rebased or reset away)" -f $t)
            $existingTags.Remove([string]$t)
        }
    }
    else {
        Write-Host ("[PublishRelease] {0} tag(s) point at commits no branch contains (rebased or reset away)." -f $orphanTags.Count)
        Write-Host ("[PublishRelease] Nobody can fetch these builds. -CreateTags removes them:")
        foreach ($t in ($orphanTags | Select-Object -First 5)) { Write-Host "    $t" }
    }
}
if ($staleTags.Count -gt 0) {
    if ($CreateTags) {
        foreach ($t in $staleTags) {
            $null = Invoke-Git @('tag', '-d', [string]$t)
            Write-Host ("[PublishRelease] Removed out-of-range tag {0}" -f $t)
            $existingTags.Remove([string]$t)
        }
        Write-Host "[PublishRelease] Those deletions are LOCAL. A pushed tag stays on the remote and comes back on the"
        Write-Host "[PublishRelease] next fetch --tags. It keeps working for anyone pinned to it, it is just no longer"
        Write-Host "[PublishRelease] listed. To retire them for real:"
        foreach ($t in ($staleTags | Select-Object -First 5)) {
            Write-Host ("    git push origin :refs/tags/{0}" -f $t)
        }
        if ($staleTags.Count -gt 5) { Write-Host ("    ... and {0} more" -f ($staleTags.Count - 5)) }
    }
    else {
        Write-Host ("[PublishRelease] {0} local build-* tag(s) name a release the index no longer offers; -CreateTags removes them:" -f $staleTags.Count)
        foreach ($t in ($staleTags | Select-Object -First 10)) { Write-Host "    $t" }
    }
}

$toTag = @($candidates | Where-Object { -not $existingTags.ContainsKey([string]$_.Tag) })
$untagged = @($sorted | Where-Object { -not $existingTags.ContainsKey([string]$_.Tag) })
$olderUntagged = $untagged.Count - $toTag.Count

if ($toTag.Count -eq 0) {
    Write-Host ("[PublishRelease] Every release in the tagging window already has its build-* tag ({0} older one(s) left untagged)." -f $olderUntagged)
}
elseif ($CreateTags) {
    foreach ($e in $toTag) {
        $null = Invoke-Git @('tag', [string]$e.Tag, [string]$e.Commit)
        Write-Host ("[PublishRelease] Tagged {0} -> {1}" -f $e.Tag, ([string]$e.Commit).Substring(0, 8))
    }
    Write-Host ("[PublishRelease] Created {0} tag(s); {1} older release(s) left untagged." -f $toTag.Count, $olderUntagged)
    Write-Host "[PublishRelease] Tags are local. Push them when you are ready:"
    Write-Host "    git push origin --tags"
}
else {
    Write-Host ("[PublishRelease] {0} release(s) in the tagging window have no build-* tag. Re-run with -CreateTags to create them locally:" -f $toTag.Count)
    foreach ($e in ($toTag | Select-Object -First 10)) {
        Write-Host ("    {0} -> {1}  {2}" -f $e.Tag, ([string]$e.Commit).Substring(0, 8), $e.Notes)
    }
    if ($toTag.Count -gt 10) { Write-Host ("    ... and {0} more in the window" -f ($toTag.Count - 10)) }
    Write-Host ("    ({0} older release(s) outside the window; widen with -TagLimit / -TagSince)" -f $olderUntagged)
}

Write-Host ("[PublishRelease] releases/index.json: {0} release(s)." -f $sorted.Count)
