<#
.SYNOPSIS
    Runs the VPB developer test suites.

.DESCRIPTION
    Three suites live under tests/. This script runs the two headless ones directly;
    the third runs inside VaM and is armed and read back through -Runtime/-RuntimeResults.

      VPB.Tests.Static   net8.0   repository invariants (Roslyn). No VaM install needed.
      VPB.Tests.Vam      net472   the whole VPB source tree compiled against VaM's own
                                  managed assemblies. Needs a VaM install.
      VPB.Tests.Runtime  net35    a BepInEx plugin that runs inside a live VaM, for the
                                  things no headless process has: GameObjects, real
                                  textures, package-aware path resolution.

    None is referenced by VPB.csproj and none ships in VPB.dll.

.EXAMPLE
    .\tests\run.ps1
    .\tests\run.ps1 -Static
    .\tests\run.ps1 -Filter HarmonyPatch
    .\tests\run.ps1 -Report          # compact paste-ready summary, also copied to the clipboard
    .\tests\run.ps1 -InstallHooks
#>
[CmdletBinding()]
param(
    [switch] $Static,
    [switch] $Vam,
    [string] $Filter,
    [string] $Configuration = 'Debug',
    [switch] $InstallHooks,
    [switch] $NoBuild,
    [switch] $Report,
    [switch] $NoClipboard,
    [switch] $Runtime,
    [switch] $RuntimeResults
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$testsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $testsDir

function Write-Section([string] $text) {
    Write-Host ''
    Write-Host "=== $text " -ForegroundColor Cyan -NoNewline
    Write-Host ('=' * [Math]::Max(0, 60 - $text.Length)) -ForegroundColor Cyan
}

function Install-GitHooks {
    $hooksSrc = Join-Path $testsDir 'hooks'
    $hooksDst = Join-Path $repoRoot '.git\hooks'
    if (-not (Test-Path $hooksDst)) {
        Write-Host "No .git\hooks directory at $hooksDst - is this a git checkout?" -ForegroundColor Red
        return $false
    }
    foreach ($hook in @('pre-commit', 'pre-push', 'post-commit')) {
        $src = Join-Path $hooksSrc $hook
        if (-not (Test-Path $src)) { continue }
        $dst = Join-Path $hooksDst $hook
        Copy-Item -Path $src -Destination $dst -Force
        Write-Host "installed $hook" -ForegroundColor Green
    }
    Write-Host ''
    Write-Host 'pre-commit runs the Static suite; pre-push runs Static + Vam.' -ForegroundColor Gray
    Write-Host 'Bypass either with: git commit --no-verify' -ForegroundColor Gray
    return $true
}

function Get-VaMPath {
    $localProps = Join-Path $repoRoot 'VPB.local.props'
    if (Test-Path $localProps) {
        $match = Select-String -Path $localProps -Pattern '<VaMPath>(.+?)</VaMPath>' | Select-Object -First 1
        if ($match) { return $match.Matches[0].Groups[1].Value.Trim() }
    }
    foreach ($candidate in @('C:\vam', 'C:\VaM')) {
        if (Test-Path (Join-Path $candidate 'VaM_Data\Managed\Assembly-CSharp.dll')) { return $candidate }
    }
    return $null
}

if ($Runtime -or $RuntimeResults) {
    Write-Section 'in-game suite'

    $vamPath = Get-VaMPath
    if (-not $vamPath) {
        Write-Host 'No VaM install found. Set <VaMPath> in VPB.local.props.' -ForegroundColor Red
        exit 1
    }

    $pluginDir = Join-Path $vamPath 'BepInEx\plugins\VPB.Tests.Runtime'
    $reportPath = Join-Path $vamPath 'Saves\PluginData\VPB\vpb-runtime-tests.xml'

    if ($RuntimeResults) {
        if (-not (Test-Path $reportPath)) {
            Write-Host "No in-game report at $reportPath." -ForegroundColor Yellow

            # A surviving marker means the plugin was armed and never got as far as consuming it.
            # Without this branch that reads identically to "you forgot to arm it", and the real
            # cause - the plugin threw before its first yield - is only in the Unity player log,
            # which BepInEx's own log does not mirror.
            $marker = Join-Path $pluginDir 'run-tests.marker'
            $playerLog = Join-Path $env:LOCALAPPDATA '..\LocalLow\MeshedVR\VaM\output_log.txt'
            if (Test-Path $marker) {
                Write-Host 'The marker is still there, so VaM either has not run yet or the plugin died before it could run the suite.' -ForegroundColor Yellow
                Write-Host "If you did launch VaM, look for TypeLoadException / MissingMethodException here:" -ForegroundColor Gray
                Write-Host "    $([System.IO.Path]::GetFullPath($playerLog))" -ForegroundColor Gray
                Write-Host '    (BepInEx/LogOutput.log does not capture those - they kill the coroutine silently.)' -ForegroundColor Gray
            }
            else {
                Write-Host 'Arm a run with:  .\tests\run.ps1 -Runtime   then launch VaM.' -ForegroundColor Gray
            }
            exit 1
        }

        try { [xml] $xml = Get-Content -Path $reportPath -Raw }
        catch {
            Write-Host "The in-game report at $reportPath is not valid XML." -ForegroundColor Red
            Write-Host $_.Exception.Message -ForegroundColor Gray
            Write-Host 'A truncated report means VaM died mid-write. Re-arm and run again.' -ForegroundColor Gray
            exit 1
        }

        # The runner writes a single <testsuite> document element. Reading $xml.testsuites here
        # yields $null, [int]$null is 0, and a failed in-game run exits 0 - the exact green-washing
        # this reader exists to prevent. RuntimeReportContractTests pins the element name.
        $suite = $xml.DocumentElement
        # LocalName, not Name: PowerShell's XML adapter shadows .Name with the element's own
        # 'name' attribute, so $suite.Name here would return "VPB.Tests.Runtime".
        if ($null -eq $suite -or $suite.LocalName -ne 'testsuite') {
            Write-Host ("Unexpected report root '<{0}>' - expected <testsuite>." -f $(if ($suite) { $suite.LocalName } else { 'none' })) -ForegroundColor Red
            Write-Host 'The runner and this reader have drifted apart. Refusing to report a result.' -ForegroundColor Gray
            exit 1
        }

        $written = (Get-Item $reportPath).LastWriteTime
        $total = [int] $suite.tests
        $failures = [int] $suite.failures
        $skipped = [int] $suite.skipped

        Write-Host ("report written {0}" -f $written)
        Write-Host ("tests {0}  failures {1}  skipped {2}" -f $total, $failures, $skipped)

        if ($total -le 0) {
            Write-Host 'The report contains no test cases. Nothing was verified.' -ForegroundColor Red
            exit 1
        }

        foreach ($case in $xml.SelectNodes('//testcase[failure]')) {
            Write-Host ''
            Write-Host ("FAILED  {0}.{1}" -f $case.classname, $case.name) -ForegroundColor Red
            Write-Host $case.failure.message
        }
        foreach ($case in $xml.SelectNodes('//testcase[skipped]')) {
            Write-Host ("SKIPPED {0}.{1} :: {2}" -f $case.classname, $case.name, $case.skipped.message) -ForegroundColor Yellow
        }

        # Count the elements too: a failures attribute that disagrees with the cases is itself a bug.
        $failureNodes = @($xml.SelectNodes('//testcase[failure]')).Count
        if ($failureNodes -ne $failures) {
            Write-Host ("Report is inconsistent: failures attribute {0}, failure elements {1}." -f $failures, $failureNodes) -ForegroundColor Red
            exit 1
        }

        if ($failures -gt 0) {
            Write-Host ''
            Write-Host ("{0} in-game test(s) failed." -f $failures) -ForegroundColor Red
            exit 1
        }

        if ($skipped -eq $total) {
            Write-Host ''
            Write-Host 'Every in-game test skipped - nothing was actually verified.' -ForegroundColor Yellow
            Write-Host 'Read the reasons above; usually the install has no packages or no built index.' -ForegroundColor Gray
            exit 1
        }

        Write-Host ''
        Write-Host ("{0} passed, {1} skipped." -f ($total - $failures - $skipped), $skipped) -ForegroundColor Green
        exit 0
    }

    if (-not (Test-Path $pluginDir)) {
        Write-Host "The in-game suite is not deployed to $pluginDir." -ForegroundColor Red
        Write-Host 'Build VPB.csproj in Debug - that deploys it.' -ForegroundColor Gray
        exit 1
    }

    New-Item -ItemType File -Path (Join-Path $pluginDir 'run-tests.marker') -Force | Out-Null
    Write-Host ("Armed. {0} created." -f (Join-Path $pluginDir 'run-tests.marker')) -ForegroundColor Green
    Write-Host ''
    Write-Host 'This does NOT run the tests. Launch VaM; the suite runs once after the scene settles,' -ForegroundColor Yellow
    Write-Host 'deletes the marker, and writes its report. Then read it with:' -ForegroundColor Yellow
    Write-Host '    .\tests\run.ps1 -RuntimeResults' -ForegroundColor Gray
    exit 0
}

if ($InstallHooks) {
    Write-Section 'installing git hooks'
    if (Install-GitHooks) { exit 0 } else { exit 1 }
}

$runStatic = $true
$runVam = $true
if ($Static -and -not $Vam) { $runVam = $false }
if ($Vam -and -not $Static) { $runStatic = $false }

$projects = @()
if ($runStatic) { $projects += [pscustomobject]@{ Name = 'VPB.Tests.Static'; Path = Join-Path $testsDir 'VPB.Tests.Static\VPB.Tests.Static.csproj' } }
if ($runVam) { $projects += [pscustomobject]@{ Name = 'VPB.Tests.Vam'; Path = Join-Path $testsDir 'VPB.Tests.Vam\VPB.Tests.Vam.csproj' } }

function Read-TrxResults([string] $trxPath) {
    $results = [pscustomobject]@{ Total = 0; Passed = 0; Failed = 0; Skipped = 0; Failures = @() }
    if (-not (Test-Path $trxPath)) { return $results }

    [xml] $doc = Get-Content -Path $trxPath -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

    foreach ($node in $doc.SelectNodes('//t:UnitTestResult', $ns)) {
        $results.Total++
        switch ($node.outcome) {
            'Passed' { $results.Passed++ }
            'Failed' {
                $results.Failed++
                $message = $node.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
                $results.Failures += [pscustomobject]@{
                    Name    = $node.testName
                    Message = if ($message) { $message.InnerText.Trim() } else { '(no message)' }
                }
            }
            default { $results.Skipped++ }
        }
    }
    return $results
}

function Write-PasteableReport($rows) {
    $lines = @()
    $lines += '## VPB test run'
    $lines += ''
    $lines += ('repo {0} @ {1}' -f (Split-Path -Leaf $repoRoot), (& git -C $repoRoot rev-parse --short HEAD 2>$null))
    $lines += ''
    $lines += '| suite | total | passed | failed | skipped |'
    $lines += '|---|---|---|---|---|'
    foreach ($row in $rows) {
        $r = $row.Results
        $lines += ('| {0} | {1} | {2} | {3} | {4} |' -f $row.Name, $r.Total, $r.Passed, $r.Failed, $r.Skipped)
    }

    $failed = @($rows | ForEach-Object { $_.Results.Failures })
    if ($failed.Count -eq 0) {
        $lines += ''
        $lines += 'All tests passed.'
    }
    else {
        foreach ($row in $rows) {
            foreach ($failure in $row.Results.Failures) {
                $lines += ''
                $lines += ('### FAILED  {0}' -f $failure.Name)
                $lines += '```'
                $lines += $failure.Message
                $lines += '```'
            }
        }
    }
    return ($lines -join [Environment]::NewLine)
}

$overall = 0
$summaries = @()
$reportRows = @()

foreach ($project in $projects) {
    Write-Section $project.Name

    $arguments = @('test', $project.Path, '--nologo', '-c', $Configuration, '-v', 'q')
    if ($Filter) { $arguments += @('--filter', "FullyQualifiedName~$Filter") }
    if ($NoBuild) { $arguments += '--no-build' }

    $trxName = "$($project.Name).trx"
    $trxDir = Join-Path $testsDir 'TestResults'
    $trxPath = Join-Path $trxDir $trxName
    if ($Report) {
        if (-not (Test-Path $trxDir)) { New-Item -ItemType Directory -Path $trxDir | Out-Null }
        if (Test-Path $trxPath) { Remove-Item $trxPath -Force }
        $arguments += @('--logger', "trx;LogFileName=$trxName", '--results-directory', $trxDir)
    }

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & dotnet @arguments
    $exit = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    $text = ($output | Out-String)

    if ($Report) {
        $reportRows += [pscustomobject]@{ Name = $project.Name; Results = (Read-TrxResults $trxPath) }
    }
    else {
        Write-Host $text
    }

    $discoveredNothing = $false
    if ($text -match 'No test is available') { $discoveredNothing = $true }
    if ($text -match 'Skipping:\s+VPB\.Tests') { $discoveredNothing = $true }

    if ($discoveredNothing) {
        Write-Host 'ZERO tests were discovered in this project.' -ForegroundColor Red
        Write-Host 'For VPB.Tests.Vam this almost always means the VaM assemblies were not copied next to' -ForegroundColor Yellow
        Write-Host 'the test DLL. Keep <Private>true</Private> on the VaM <Reference> items.' -ForegroundColor Yellow
        $exit = 1
    }

    $line = ($output | Select-String -Pattern '^(Passed!|Failed!)' | Select-Object -Last 1)
    if ($line) {
        $summaries += "$($project.Name): $($line.ToString().Trim())"
    }
    else {
        $summaries += "$($project.Name): no result line (exit $exit)"
    }

    if ($exit -ne 0) { $overall = $exit }
}

if ($Report) {
    $markdown = Write-PasteableReport $reportRows
    $outPath = Join-Path (Join-Path $testsDir 'TestResults') 'summary.md'
    Set-Content -Path $outPath -Value $markdown -Encoding utf8

    Write-Host ''
    Write-Host $markdown
    Write-Host ''

    if (-not $NoClipboard) {
        try {
            $markdown | Set-Clipboard
            Write-Host 'Copied to the clipboard. Also written to tests\TestResults\summary.md' -ForegroundColor Green
        }
        catch {
            Write-Host "Written to $outPath (clipboard unavailable)" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "Written to $outPath" -ForegroundColor Green
    }

    exit $overall
}

Write-Section 'summary'
foreach ($s in $summaries) {
    if ($s -match 'Failed!|no result line') { Write-Host $s -ForegroundColor Red }
    else { Write-Host $s -ForegroundColor Green }
}

if ($overall -ne 0) {
    Write-Host ''
    Write-Host 'Some tests failed. Each failure message says what the defect means in the game' -ForegroundColor Yellow
    Write-Host 'and, where an exclusion is legitimate, which tests/known-*.txt file to add it to.' -ForegroundColor Yellow
}

exit $overall
