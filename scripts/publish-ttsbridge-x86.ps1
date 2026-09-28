$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\LoquendoAI.TtsBridge32\LoquendoAI.TtsBridge32.csproj'
$out = Join-Path $root 'artifacts\ttsbridge-win-x86'

Write-Host 'Publishing LoquendoAI.TtsBridge32 as self-contained win-x86...'
dotnet publish $project -c Release -r win-x86 --self-contained true -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $out 'LoquendoAI.TtsBridge32.exe'
Write-Host ''
Write-Host "Ready: $exe"
Write-Host 'This build does NOT require an x86 .NET runtime installed system-wide.'
