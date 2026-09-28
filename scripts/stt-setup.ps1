<#
.SYNOPSIS
  Instala la transcripción local (faster-whisper) de Loquendo AI. No pide nada: si falta Python, lo trae.

.DESCRIPTION
  Orden en el que busca el Python que usará la app (LocalTranscriptionClient hace la misma búsqueda):
    1. worker\python\python.exe   → Python embebido (el que trae la versión publicada: portable e instalador).
    2. worker\.venv               → entorno virtual con el Python del sistema (desarrollo desde el código).
  Si no hay ninguno: crea el venv con el Python del sistema (3.9+ de 64 bits) o, si no hay Python en el equipo,
  si ese camino falla o si se pide -Embebido, descarga el Python embebido oficial de python.org en worker\python.
  Después instala worker\stt\requirements.txt con pip y comprueba que faster-whisper se importa.

  El Python embebido se prepara en una carpeta temporal y solo se pone en worker\python cuando está completo, así
  una instalación cancelada no deja un Python a medias. Un worker\python sin pip se vuelve a instalar.

  scripts\publicar.ps1 lo llama con -Embebido para dejar la transcripción lista dentro de la carpeta portable y del
  instalador; la app lo ejecuta sola (preguntando antes) si falta. El modelo de Whisper se descarga la primera vez
  que se transcribe.

.EXAMPLE
  .\scripts\stt-setup.ps1
  .\scripts\stt-setup.ps1 -Carpeta .\artifacts\LoquendoAI_v1.4.0_portable -Embebido -Cache .\artifacts\cache
#>
param(
    [string]$Carpeta,
    [switch]$Embebido,
    [string]$PythonVersion = '3.12.10',
    [string]$Cache
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is many times faster without the progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$projectDir = if ($Carpeta) { (Resolve-Path $Carpeta).Path } else { (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
$requirements = Join-Path $projectDir 'worker\stt\requirements.txt'
if (-not (Test-Path $requirements)) { throw "No se encontró $requirements" }
$embedded = Join-Path $projectDir 'worker\python'
$embeddedExe = Join-Path $embedded 'python.exe'
$venv = Join-Path $projectDir 'worker\.venv'
$venvExe = Join-Path $venv 'Scripts\python.exe'
if (-not $Cache) { $Cache = Join-Path $env:TEMP 'LoquendoAI-stt' }
New-Item -ItemType Directory -Force -Path $Cache | Out-Null

# Native programs (python, pip) write warnings to stderr; with 'Stop' Windows PowerShell 5.1 can turn those lines into
# errors when the output is redirected (the app runs this script hidden). Only their exit code decides.
function Native([string]$exe, [string[]]$arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $exe @arguments | Out-Host } finally { $ErrorActionPreference = $previous }
    return ($LASTEXITCODE -eq 0)
}

function Download([string]$url, [string]$file) {
    if (Test-Path $file) { return }
    Write-Host "Descargando $url"
    $partial = "$file.part"
    Invoke-WebRequest -Uri $url -OutFile $partial -UseBasicParsing
    Move-Item $partial $file -Force
}

function SystemPython {
    # A usable 64-bit Python 3.9+ of the system (never the Microsoft Store stub, which only opens the Store;
    # a 32-bit Python cannot install faster-whisper).
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        foreach ($candidate in @(@('py', '-3'), @('python'))) {
            $exe = $candidate[0]
            if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) { continue }
            $arguments = @($candidate | Select-Object -Skip 1) + @('-c', 'import sys; print(sys.version_info >= (3, 9) and sys.maxsize > 2**32)')
            $ok = & $exe @arguments 2>$null
            if ($LASTEXITCODE -eq 0 -and "$ok".Trim() -eq 'True') { return ,$candidate }
        }
    } finally { $ErrorActionPreference = $previous }
    return $null
}

function InstallEmbedded {
    Write-Host "Instalando Python $PythonVersion embebido en worker\python (no toca el Python del sistema)…"
    $zip = Join-Path $Cache "python-$PythonVersion-embed-amd64.zip"
    Download "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip" $zip
    $staging = "$embedded.nuevo"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $staging -Force
    # The embeddable Python ignores site-packages until «import site» is enabled in its ._pth file.
    $pth = Get-ChildItem $staging -Filter 'python*._pth' | Select-Object -First 1
    if (-not $pth) { throw 'El Python embebido no trae su archivo ._pth.' }
    $lines = @(Get-Content $pth.FullName | Where-Object { $_ -notmatch '^\s*#?\s*import site\s*$' -and $_ -ne 'Lib\site-packages' })
    $lines += 'Lib\site-packages'
    $lines += 'import site'
    Set-Content -Path $pth.FullName -Value $lines -Encoding ASCII
    $getPip = Join-Path $Cache 'get-pip.py'
    Download 'https://bootstrap.pypa.io/get-pip.py' $getPip
    if (-not (Native (Join-Path $staging 'python.exe') @($getPip, '--no-warn-script-location', '--disable-pip-version-check'))) {
        throw 'No se pudo instalar pip en el Python embebido (¿hay conexión a internet?).'
    }
    # Complete: only now it replaces worker\python.
    if (Test-Path $embedded) { Remove-Item $embedded -Recurse -Force }
    Move-Item $staging $embedded
}

function InstallRequirements([string]$python) {
    Write-Host 'Instalando faster-whisper (puede tardar unos minutos la primera vez)…'
    if (-not (Native $python @('-m', 'pip', 'install', '--no-warn-script-location', '--disable-pip-version-check', '-r', $requirements))) {
        return $false
    }
    return (Native $python @('-c', 'import faster_whisper'))
}

$python = $null
if (Test-Path $embeddedExe) {
    # A Python left half-way (an old cancelled install) is replaced.
    if (-not (Native $embeddedExe @('-m', 'pip', '--version'))) { InstallEmbedded }
    $python = $embeddedExe
} elseif (-not $Embebido -and (Test-Path $venvExe)) {
    $python = $venvExe
} else {
    $system = if ($Embebido) { $null } else { SystemPython }
    if ($system) {
        Write-Host 'Creando el entorno worker\.venv con el Python del sistema…'
        $arguments = @($system | Select-Object -Skip 1) + @('-m', 'venv', $venv)
        if (Native $system[0] $arguments) { $python = $venvExe }
    }
    if (-not $python) {
        InstallEmbedded
        $python = $embeddedExe
    }
}

if (-not (InstallRequirements $python)) {
    if ($python -eq $venvExe -and -not (Test-Path $embeddedExe)) {
        # The system Python could not do it (e.g. no wheels for that version): the embedded one can.
        Write-Host 'El Python del sistema no pudo instalar faster-whisper; se usa el Python embebido.'
        InstallEmbedded
        $python = $embeddedExe
        if (InstallRequirements $python) { Write-Host "Transcripción local lista ($python)."; exit 0 }
    }
    throw 'No se pudo instalar faster-whisper (revisa la conexión a internet). Si el error menciona una DLL, instala ' +
        '«Microsoft Visual C++ 2015-2022 Redistributable (x64)».'
}
Write-Host "Transcripción local lista ($python)."
exit 0
