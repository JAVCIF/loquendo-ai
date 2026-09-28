$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe'
if (-not (Test-Path $exe)) {
    & (Join-Path $PSScriptRoot 'publish-ttsbridge-x86.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
& $exe scan @args
exit $LASTEXITCODE
