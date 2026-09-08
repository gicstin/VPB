param(
    [Parameter(Mandatory = $true)][string] $VaMPath,
    [ValidateSet('All', 'Manifest', 'Cleanup')][string] $Check = 'All'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('vpb-install-check-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
try {
    if ($Check -ne 'Cleanup') {
        $patch = Join-Path $scratch 'vam_patch'
        $net = Join-Path $patch 'BepInEx/plugins/VPB/net'
        [void][IO.Directory]::CreateDirectory($net)
        foreach ($name in @('VpbNet.exe', 'steam_api64.dll')) {
            [IO.File]::WriteAllText((Join-Path $net $name), 'fixture')
        }
        $manifest = Join-Path $patch 'patch_manifest.json'
        '[{"RelativePath":"BepInEx/plugins/VPB/VPB.dll","IsDirectory":false},{"RelativePath":"BepInEx/plugins/VPB/native","IsDirectory":true}]' | Set-Content -LiteralPath $manifest
        & (Join-Path $PSScriptRoot 'SyncPatchManifestVpbNet.ps1') -ProjectDir $scratch
        $entries = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        if ($entries.Count -ne 4 -or $entries.RelativePath -notcontains 'BepInEx/plugins/VPB/VPB.dll' -or $entries.RelativePath -notcontains 'BepInEx/plugins/VPB/native') {
            throw 'Broker manifest sync discarded unrelated install entries.'
        }
        $first = [IO.File]::ReadAllText($manifest)
        & (Join-Path $PSScriptRoot 'SyncPatchManifestVpbNet.ps1') -ProjectDir $scratch
        if ([IO.File]::ReadAllText($manifest) -ne $first) { throw 'Broker manifest sync is not idempotent.' }
        Write-Output 'Manifest preservation and repeat sync passed.'
    }
    if ($Check -ne 'Manifest') {
        if (Get-Process VpbNet -ErrorAction SilentlyContinue) { throw 'Close multiplayer broker before testing old cleanup binaries.' }
        $plugins = Join-Path $scratch 'BepInEx/plugins'
        $vpb = Join-Path $plugins 'VPB'
        $net = Join-Path $vpb 'net'
        $old = Join-Path $plugins 'VpbNet'
        [void][IO.Directory]::CreateDirectory($net)
        [void][IO.Directory]::CreateDirectory($old)
        foreach ($name in @('VPB.dll', 'keep.txt', 'net/VpbNet.exe', 'net/steam_api64.dll', 'net/stale.txt')) {
            [IO.File]::WriteAllText((Join-Path $vpb $name), 'fixture')
        }
        $rows = @('VPB.dll', 'keep.txt', 'patch_manifest.json', 'net/VpbNet.exe', 'net/steam_api64.dll') | ForEach-Object {
            [pscustomobject]@{ RelativePath = 'BepInEx/plugins/VPB/' + $_; IsDirectory = $false }
        }
        $rows | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $vpb 'patch_manifest.json')
        [void][Reflection.Assembly]::LoadFrom((Join-Path $VaMPath 'BepInEx/core/BepInEx.dll'))
        [void][Reflection.Assembly]::LoadFrom((Join-Path $VaMPath 'BepInEx/core/Mono.Cecil.dll'))
        $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'bin/Release/VPB.Patcher.dll'))
        $type = $assembly.GetType('VPB.Patcher.VPBPatcher', $true)
        $flags = [Reflection.BindingFlags]'NonPublic,Static'
        $log = New-Object BepInEx.Logging.ManualLogSource 'InstallCheck'
        $type.GetField('Log', $flags).SetValue($null, $log)
        [void]$type.GetMethod('PruneLegacyLayout', $flags).Invoke($null, [object[]]@([string]$scratch))
        foreach ($name in @('VpbNet.exe', 'steam_api64.dll')) {
            if (-not (Test-Path -LiteralPath (Join-Path $net $name))) { throw "Startup cleanup removed active multiplayer payload: $name" }
        }
        if (Test-Path -LiteralPath $old) { throw 'Legacy broker directory was not retired.' }
        if (Test-Path -LiteralPath (Join-Path $net 'stale.txt')) { throw 'Unshipped broker file was not pruned.' }
        Write-Output 'Startup cleanup preserves multiplayer and removes stale files.'
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'vpb-install-check-*') {
        throw 'Refusing cleanup outside test directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
