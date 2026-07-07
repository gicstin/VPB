param(
    [Parameter(Mandatory = $true)]
    [string] $VersionFile,
    [Parameter(Mandatory = $true)]
    [string] $IntermediateDir,
    [Parameter(Mandatory = $true)]
    [string] $ProjectDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Normalize-Base([string] $s) {
    return ($s -replace '^[vV]', '').Trim()
}

function Escape-CSharpString([string] $s) {
    return (($s -replace '\\', '\\\\') -replace '"', '\"')
}

function Write-TwoLineVersionFile([string] $path, [string] $line1, [int] $line2) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    $content = $line1.TrimEnd() + "`r`n" + [string]$line2
    [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
}

function Write-PluginVersionCs([string] $path, [string] $baseSemVer, [int] $n, [string] $fullVersion) {
    $eb = Escape-CSharpString $baseSemVer
    $ef = Escape-CSharpString $fullVersion
    $ns = [string]$n
    # UTC date only (no time) so the tooltip can show build age without leaking timezone/work hours.
    $buildDate = [System.DateTime]::UtcNow.ToString('yyyy-MM-dd')
    $lines = @(
        'namespace VPB {',
        '    internal static class PluginVersionInfo {',
        "        public const string BaseVersion = `"$eb`";",
        "        public const string BuildNumber = `"$ns`";",
        "        public const string BuildVersion = `"b$ns`";",
        "        public const string Version = `"$ef`";",
        "        public const string BuildDate = `"$buildDate`";",
        '    }',
        '}'
    )
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($path, ($lines -join "`r`n") + "`r`n", $utf8NoBom)
}

if (-not (Test-Path -LiteralPath $VersionFile)) {
    Write-Error "Version file not found: $VersionFile"
    exit 1
}

$raw = [System.IO.File]::ReadAllText($VersionFile)
$lines = @(
    $raw -split "`r`n|`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
)

if ($lines.Count -lt 2) {
    Write-Error "plugin_version.txt must have two lines: base version (e.g. 0.28.0), then build number (integer)."
    exit 1
}

$baseLine = $lines[0]
$baseSemVer = Normalize-Base $baseLine
if ($baseSemVer -eq '') {
    Write-Error "Base version line is empty after trimming."
    exit 1
}

$buildText = $lines[1]
if ($buildText -notmatch '^\d+$') {
    Write-Error "Second line must be a non-negative integer build counter (got: '$buildText')."
    exit 1
}

$n = [int]$buildText
if ($n -lt 0) {
    Write-Error "Build counter must be non-negative."
    exit 1
}

if (-not (Test-Path -LiteralPath $IntermediateDir)) {
    New-Item -ItemType Directory -Path $IntermediateDir -Force | Out-Null
}

# Shared across Debug/Release so switching configuration still detects base-line changes.
$objDir = Join-Path $ProjectDir 'obj'
if (-not (Test-Path -LiteralPath $objDir)) {
    New-Item -ItemType Directory -Path $objDir -Force | Out-Null
}
$cacheFile = Join-Path $objDir 'VPB_last_plugin_base.txt'
$lastNorm = $null
if (Test-Path -LiteralPath $cacheFile) {
    $lastNorm = Normalize-Base ([System.IO.File]::ReadAllText($cacheFile))
}

if ($lastNorm -and ($lastNorm -ne $baseSemVer)) {
    $n = 1
    Write-TwoLineVersionFile $VersionFile $baseLine $n
}

$fullVersion = "$baseSemVer.$n"
$outCs = Join-Path $IntermediateDir 'PluginVersion.g.cs'
Write-PluginVersionCs $outCs $baseSemVer $n $fullVersion

$nNext = $n + 1
Write-TwoLineVersionFile $VersionFile $baseLine $nNext

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($cacheFile, $baseSemVer, $utf8NoBom)
