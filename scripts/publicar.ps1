<#
.SYNOPSIS
  Crea la versión final de Loquendo AI para distribuir: el .exe, la carpeta portable, su .zip y, si está
  instalado Inno Setup, el instalador.

.DESCRIPTION
  1. (Opcional, -Pruebas) pasa las pruebas automáticas (las mismas de scripts\pruebas.cmd).
  2. Publica la app (LoquendoAI.exe, x64) como un solo archivo que ya lleva .NET dentro: el usuario no instala nada.
  3. Publica el puente TTS x86 (Loquendo TTS7 y SAPI son de 32 bits) en su propia carpeta «tts-bridge».
  4. Arma artifacts\LoquendoAI_v<versión>_portable\ con lo que la app busca junto al .exe:
       tts-bridge\, worker\stt\, scripts\vegas\, tools\balcon\ (solo el README: BALCON no se redistribuye),
       documentación y «portable.txt». Con «portable.txt» la app guarda su configuración en «datos\» junto al .exe
       en vez de %LOCALAPPDATA%.
  5. Transcripción local YA INSTALADA (1.4.0): scripts\stt-setup.ps1 -Embebido pone un Python embebido oficial con
     faster-whisper en worker\python (≈ 300 MB). El usuario no ejecuta nada; el modelo de Whisper se descarga solo
     la primera vez que transcribe. -SinSTT lo omite (la app lo instala sola cuando haga falta, con su permiso).
     Necesita internet la primera vez; las descargas quedan en artifacts\cache para la próxima publicación.
     Junto al Python van las DLL del runtime de Visual C++ (despliegue local permitido por Microsoft), así
     faster-whisper funciona también en un Windows que nunca instaló el «Visual C++ Redistributable».
  5b. FFmpeg INCLUIDO (1.4.2): descarga la compilación GPL «shared» de BtbN/FFmpeg-Builds de la serie estable
     -FFmpegSerie (por defecto 8.1, la probada; el parche más reciente de esa serie) y la deja en tools\ffmpeg con su
     licencia, la versión exacta y el enlace a su código fuente. La app la usa antes que cualquier FFmpeg del PATH.
     -SinFFmpeg lo omite (entonces hace falta FFmpeg en el PATH). -FFmpegZip usa un .zip ya descargado.
  6. Comprime la carpeta portable en artifacts\LoquendoAI_v<versión>_portable_win-x64.zip.
  7. Con -Instalador (y Inno Setup 6 instalado) compila scripts\instalador.iss → artifacts\LoquendoAI_v<versión>_setup.exe,
     que instala exactamente lo mismo (también la transcripción local).

.EXAMPLE
  .\scripts\publicar.ps1
  .\scripts\publicar.ps1 -Pruebas -Instalador
  .\scripts\publicar.ps1 -DependeDeNet     # .exe pequeño; el usuario necesita el runtime .NET 10 Desktop instalado
  .\scripts\publicar.ps1 -SinSTT           # sin la transcripción local incluida (paquete ≈ 300 MB más pequeño)
  .\scripts\publicar.ps1 -SinFFmpeg        # sin FFmpeg (el usuario lo pone en el PATH)
  .\scripts\publicar.ps1 -FFmpegSerie 9.0  # otra serie estable de FFmpeg
#>
param(
    [switch]$Pruebas,
    [switch]$Instalador,
    [switch]$DependeDeNet,
    [switch]$SinSTT,
    [switch]$SinFFmpeg,
    [string]$FFmpegZip,
    [string]$FFmpegSerie = '8.1',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    if (-not $Version) { throw 'No se encontró <Version> en Directory.Build.props; pásala con -Version 1.2.3' }
}
$name = "LoquendoAI_v$Version"
$artifacts = Join-Path $root 'artifacts'
$work = Join-Path $artifacts 'publicar'
$portable = Join-Path $artifacts "${name}_portable"
$zip = Join-Path $artifacts "${name}_portable_win-x64.zip"

function Step($text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Falló: $exe $($arguments -join ' ')" }
}

Step "Loquendo AI $Version"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Falta el SDK de .NET 10 (https://dotnet.microsoft.com/download).' }

if ($Pruebas) {
    Step 'Pruebas automáticas'
    Run 'dotnet' @('run', '--project', '.\tests\LoquendoAI.Tests\LoquendoAI.Tests.csproj', '-c', 'Release')
}

Remove-Item $work, $portable -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $zip) { Remove-Item $zip -Force }

Step 'App (x64)'
$selfContained = if ($DependeDeNet) { 'false' } else { 'true' }
$appArguments = @('publish', '.\src\LoquendoAI.App\LoquendoAI.App.csproj', '-c', 'Release', '-r', 'win-x64',
    '--self-contained', $selfContained, '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none', '-p:DebugSymbols=false', '-o', (Join-Path $work 'app'))
# Compressing the bundle is only allowed when .NET goes inside it (NETSDK1176 otherwise).
if (-not $DependeDeNet) { $appArguments += '-p:EnableCompressionInSingleFile=true' }
Run 'dotnet' $appArguments

Step 'Puente TTS (x86)'
Run 'dotnet' @('publish', '.\src\LoquendoAI.TtsBridge32\LoquendoAI.TtsBridge32.csproj', '-c', 'Release', '-r', 'win-x86',
    '--self-contained', 'true', '-p:DebugType=none', '-p:DebugSymbols=false', '-o', (Join-Path $work 'tts-bridge'))

Step "Carpeta portable: $portable"
New-Item -ItemType Directory -Force -Path $portable | Out-Null
$appExe = Get-ChildItem (Join-Path $work 'app') -Filter 'LoquendoAI.App.exe' | Select-Object -First 1
if (-not $appExe) { throw 'No se generó LoquendoAI.App.exe' }
# The single-file bundle keeps the assembly name inside, so the file can be renamed.
Copy-Item $appExe.FullName (Join-Path $portable 'LoquendoAI.exe')
Get-ChildItem (Join-Path $work 'app') -Exclude 'LoquendoAI.App.exe', '*.pdb' | Copy-Item -Destination $portable -Recurse -Force
Copy-Item (Join-Path $work 'tts-bridge') (Join-Path $portable 'tts-bridge') -Recurse -Force

New-Item -ItemType Directory -Force -Path (Join-Path $portable 'worker\stt'), (Join-Path $portable 'scripts\vegas'), (Join-Path $portable 'tools\balcon') | Out-Null
Copy-Item '.\worker\stt\transcribe.py', '.\worker\stt\requirements.txt', '.\worker\stt\requirements-gpu.txt' (Join-Path $portable 'worker\stt') -Force
Copy-Item '.\scripts\stt-setup.ps1' (Join-Path $portable 'scripts') -Force
Copy-Item '.\scripts\vegas\*.cs' (Join-Path $portable 'scripts\vegas') -Force
Copy-Item '.\tools\balcon\README.txt' (Join-Path $portable 'tools\balcon') -Force
Copy-Item '.\README.md', '.\CHANGELOG.md', '.\LICENSE', '.\THIRD_PARTY_NOTICES.md' $portable -Force
Copy-Item '.\src\LoquendoAI.App\Assets\loquendo-ai.ico' $portable -Force

if (-not $SinSTT) {
    Step 'Transcripción local (Python embebido + faster-whisper)'
    $cache = Join-Path $artifacts 'cache'
    & (Join-Path $root 'scripts\stt-setup.ps1') -Carpeta $portable -Embebido -Cache $cache
    # Byte-code caches are rebuilt on the user's PC; they only make the package bigger.
    Get-ChildItem (Join-Path $portable 'worker\python') -Directory -Recurse -Filter '__pycache__' -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    # Visual C++ runtime next to python.exe (app-local deployment): CTranslate2/onnxruntime need msvcp140.dll & co.
    $python = Join-Path $portable 'worker\python'
    foreach ($dll in 'msvcp140.dll', 'msvcp140_1.dll', 'msvcp140_2.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'concrt140.dll') {
        $system = Join-Path $env:SystemRoot "System32\$dll"
        if ((Test-Path $system) -and -not (Test-Path (Join-Path $python $dll))) { Copy-Item $system $python }
    }
}

if (-not $SinFFmpeg) {
    Step 'FFmpeg (preview, exportación a VEGAS y análisis de audio)'
    $cache = Join-Path $artifacts 'cache'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $ffmpegSource = $null
    if (-not $FFmpegZip) {
        # A pinned stable series of BtbN/FFmpeg-Builds (GPL: the preview encodes with libx264), shared DLLs. The dated
        # autobuild release (not the rolling «latest») gives an exact version, commit and download to cite for the GPL.
        $ProgressPreference = 'SilentlyContinue'
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $headers = @{ 'User-Agent' = 'LoquendoAI-publicar' }
        # In GitHub Actions the token avoids the anonymous API limit shared by the runners.
        if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }
        $releases = Invoke-RestMethod -Uri 'https://api.github.com/repos/BtbN/FFmpeg-Builds/releases?per_page=15' -UseBasicParsing -Headers $headers
        $pattern = '^ffmpeg-n(?<v>[\d.]+)(-\d+-g(?<commit>[0-9a-f]+))?-win64-gpl-shared-' + [regex]::Escape($FFmpegSerie) + '\.zip$'
        $asset = $null
        foreach ($release in ($releases | Where-Object { $_.tag_name -like 'autobuild-*' })) {
            $asset = $release.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
            if ($asset) { $ffmpegRelease = $release; break }
        }
        if (-not $asset) {
            throw "No hay compilación de FFmpeg $FFmpegSerie para Windows en las últimas versiones de BtbN/FFmpeg-Builds. Prueba otra serie con -FFmpegSerie (por ejemplo 9.0), usa -FFmpegZip o -SinFFmpeg."
        }
        $FFmpegZip = Join-Path $cache $asset.name
        if (-not (Test-Path $FFmpegZip)) {
            Write-Host "Descargando $($asset.browser_download_url)"
            Invoke-WebRequest -Uri $asset.browser_download_url -OutFile "$FFmpegZip.part" -UseBasicParsing
            Move-Item "$FFmpegZip.part" $FFmpegZip -Force
        }
        $ffmpegSource = $ffmpegRelease.html_url
    }
    $extracted = Join-Path $work 'ffmpeg'
    Expand-Archive -Path $FFmpegZip -DestinationPath $extracted -Force
    $bin = Get-ChildItem $extracted -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $bin) { throw "El zip de FFmpeg no trae ffmpeg.exe: $FFmpegZip" }
    $target = Join-Path $portable 'tools\ffmpeg'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem $bin.DirectoryName | Where-Object { $_.Name -ne 'ffplay.exe' } | Copy-Item -Destination $target -Force
    $license = Get-ChildItem $extracted -Recurse -Filter 'LICENSE*' | Select-Object -First 1
    if ($license) { Copy-Item $license.FullName (Join-Path $target 'LICENSE.txt') -Force }
    $zipName = [IO.Path]::GetFileName($FFmpegZip)
    $versionLine = (& (Join-Path $target 'ffmpeg.exe') -hide_banner -version | Select-Object -First 1)
    $commit = [regex]::Match($zipName, '-g([0-9a-f]{7,})-').Groups[1].Value
    @"
FFmpeg incluido con Loquendo AI.

Archivo:  $zipName
Versión:  $versionLine
Origen:   $(if ($ffmpegSource) { $ffmpegSource } else { 'https://github.com/BtbN/FFmpeg-Builds/releases' })

Compilación de BtbN/FFmpeg-Builds (https://github.com/BtbN/FFmpeg-Builds), licencia GPL v3, con libx264.
FFmpeg es de sus autores (https://ffmpeg.org); Loquendo AI no lo modifica y solo lo ejecuta como programa aparte.

Código fuente correspondiente:
- FFmpeg: $(if ($commit) { "commit $commit de https://git.ffmpeg.org/ffmpeg.git (espejo: https://github.com/FFmpeg/FFmpeg/commit/$commit)" } else { 'https://git.ffmpeg.org/ffmpeg.git' })
- Recetas de compilación y bibliotecas incluidas: https://github.com/BtbN/FFmpeg-Builds
Si alguno de estos enlaces deja de funcionar, pide el código fuente en un issue de
https://github.com/JAVCIF/loquendo-ai y se te facilitará.

Licencia: LICENSE.txt en esta carpeta.
"@ | Set-Content -Encoding UTF8 (Join-Path $target 'README.txt')
    Write-Host "FFmpeg: $versionLine"
}

@"
Loquendo AI $Version · versión portable

- Abre LoquendoAI.exe. No necesita instalar .NET$(if ($DependeDeNet) { ' (esta compilación SÍ necesita el runtime .NET 10 Desktop)' } else { '' }).
- La configuración (tema, tamaños, modelos de IA, claves, registros) se guarda en la carpeta «datos» junto al .exe
  porque existe este «portable.txt». Bórralo para usar %LOCALAPPDATA%\LoquendoAI como la versión instalada.
- Las claves de API se guardan cifradas para tu usuario de Windows: en otro PC hay que escribirlas de nuevo.
- Voces: hace falta Loquendo TTS7 (u otra voz SAPI) instalada en Windows; el puente de 32 bits está en «tts-bridge».
- BALCON (voces SAPI4) no se incluye: descárgalo de https://www.cross-plus-a.com/es/bconsole.htm y sigue tools\balcon\README.txt.
$(if ($SinSTT) { '- Transcripción local (Voces grabadas): la app la instala sola la primera vez que la uses (te pregunta antes).' } else { '- Transcripción local (Voces grabadas): ya viene instalada (worker\python). El modelo de Whisper se descarga solo la primera vez que transcribes y se guarda en «datos\modelos-stt».' })
$(if ($SinFFmpeg) { '- Preview y exportación: hace falta FFmpeg en el PATH.' } else { '- Preview y exportación: FFmpeg ya viene incluido (tools\ffmpeg).' })
- Scripts de VEGAS: scripts\vegas.
"@ | Set-Content -Encoding UTF8 (Join-Path $portable 'portable.txt')

Step "Zip: $zip"
Compress-Archive -Path $portable -DestinationPath $zip -CompressionLevel Optimal

if ($Instalador) {
    Step 'Instalador (Inno Setup)'
    # Any installed Inno Setup (6 or newer), newest folder first.
    $iscc = @((Get-Command iscc -ErrorAction SilentlyContinue).Source) +
        @(${env:ProgramFiles(x86)}, $env:ProgramFiles | Where-Object { $_ } | ForEach-Object {
            Get-ChildItem $_ -Directory -Filter 'Inno Setup *' -ErrorAction SilentlyContinue | Sort-Object Name -Descending |
                ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } }) |
        Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        Write-Warning 'No se encontró Inno Setup (https://jrsoftware.org/isdl.php): se omite el instalador.'
    } else {
        Run $iscc @("/DAppVersion=$Version", "/DSourceDir=$portable", "/DOutputDir=$artifacts", '.\scripts\instalador.iss')
    }
}

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Step 'Listo'
Write-Host "  Portable: $portable"
Write-Host "  Zip:      $zip"
if ($Instalador) { Write-Host "  Setup:    $(Join-Path $artifacts "${name}_setup.exe")" }
