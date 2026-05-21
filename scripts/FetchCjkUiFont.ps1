param(
    [Parameter(Mandatory = $true)]
    [string] $OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Noto Sans SC Regular (OFL). Used as single embedded CJK-capable UI font for Unity Text.
$url = 'https://raw.githubusercontent.com/google/fonts/main/ofl/notosanssc/static/NotoSansSC-Regular.ttf'

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

Invoke-WebRequest -Uri $url -OutFile $OutFile -UseBasicParsing
