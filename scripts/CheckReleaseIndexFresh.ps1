<#
.SYNOPSIS
  Verifies that the committed build is the newest entry in releases/index.json.

.DESCRIPTION
  Backstop for the post-commit hook. A stale index is the one failure in this pipeline that
  stays silent: the file remains internally consistent, every other test passes, and the only
  symptom is that the in-game version picker offers an old build as "latest".

  Runs against committed state, so a normal build - which leaves the working tree ahead of the
  index until you commit - never trips it. Intended for CI on the pushed tip; the local flow is
  handled by the post-commit hook, which records the build for you.
#>
param(
    [string] $ProjectDir = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

$manifestText = Invoke-Git @('show', 'HEAD:vam_patch/patch_manifest2.json')
if ($null -eq $manifestText) {
    Write-Host "[ReleaseIndex] No committed vam_patch/patch_manifest2.json yet - nothing to verify."
    exit 0
}

$indexText = Invoke-Git @('show', 'HEAD:releases/index.json')
if ($null -eq $indexText) {
    Write-Host "[ReleaseIndex] No committed releases/index.json yet - nothing to verify."
    exit 0
}

$shipped = (($manifestText -join "`n") | ConvertFrom-Json).Version
$releases = @((($indexText -join "`n") | ConvertFrom-Json).Releases)

if ([string]::IsNullOrWhiteSpace($shipped)) {
    Write-Host "[ReleaseIndex] Committed patch_manifest2.json carries no Version."
    exit 1
}
if ($releases.Count -eq 0) {
    Write-Host "[ReleaseIndex] Committed releases/index.json lists no releases."
    exit 1
}

$head = [string]$releases[0].Version
if ($head -eq $shipped) {
    Write-Host ("[ReleaseIndex] VPB {0} is recorded as the newest release." -f $shipped)
    exit 0
}

Write-Host ("[ReleaseIndex] This branch ships VPB {0} but releases/index.json lists {1} as newest." -f $shipped, $head)
Write-Host "[ReleaseIndex] The version picker would offer a stale build as 'latest' and nothing else would fail."
Write-Host "[ReleaseIndex] The post-commit hook normally records this. If hooks are not installed, run:"
Write-Host "    pwsh -File tests/run.ps1 -InstallHooks"
Write-Host "    pwsh -File scripts/PublishRelease.ps1 -CreateTags"
exit 1
