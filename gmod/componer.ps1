<#
.SYNOPSIS
  Convierte un render de Loquendo Studio (Garry's Mod) en un .png o .mov con transparencia real.

.DESCRIPTION
  Loquendo Studio guarda cada fotograma dos veces, sobre negro y sobre blanco. La diferencia entre ambos da el
  canal alfa exacto (bordes suaves, pelo, cristal), sin chroma. Este script lo hace con FFmpeg:
    - 1 fotograma  → <nombre>.png (RGBA), listo para usar como render en Loquendo AI.
    - varios       → <nombre>.mov ProRes 4444 con alfa (VEGAS lo lee con transparencia) y la voz si la hubo.
  Con fondo de color sólido sale un ProRes 422 HQ sin alfa.

.EXAMPLE
  .\componer.ps1 "C:\Program Files (x86)\Steam\steamapps\common\GarrysMod\garrysmod\data\loquendo_studio\renders\personaje"
  .\componer.ps1 <carpeta> -FFmpeg "C:\LoquendoAI\tools\ffmpeg\ffmpeg.exe"
#>
param(
    [Parameter(Mandatory = $true)][string]$Carpeta,
    [string]$FFmpeg
)
$ErrorActionPreference = 'Stop'

$info = Get-Content (Join-Path $Carpeta 'render.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $FFmpeg) {
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($cmd) { $FFmpeg = $cmd.Source }
}
if (-not $FFmpeg -or -not (Test-Path $FFmpeg)) {
    throw 'No se encontró FFmpeg. Ponlo en el PATH o pásalo con -FFmpeg (Loquendo AI trae uno en tools\ffmpeg\ffmpeg.exe).'
}

$fps = [int]$info.fps
$size = "$($info.ancho):$($info.alto)"
$single = [int]$info.frames -le 1
$out = Join-Path $Carpeta ($info.nombre + $(if ($single) { '.png' } else { '.mov' }))
$ff = @('-v', 'warning', '-y')

if ($info.modo -eq 'transparente') {
    $ff += '-framerate', $fps, '-i', (Join-Path $Carpeta 'negro_%05d.png')
    $ff += '-framerate', $fps, '-i', (Join-Path $Carpeta 'blanco_%05d.png')
    # alpha = 255 - (white - black); black = colour premultiplied by alpha → scale, then unpremultiply.
    $filter = "[0:v]format=gbrp,split[n1][n2];[1:v]format=gbrp[b];[b][n1]blend=all_expr='255-(A-B)',extractplanes=g[a];" +
        "[n2][a]alphamerge,scale=${size}:flags=lanczos,unpremultiply=inplace=1"
    $filter += $(if ($single) { ',format=rgba[v]' } else { ',format=yuva444p10le[v]' })
    $codec = if ($single) { @('-frames:v', '1') } else { @('-c:v', 'prores_ks', '-profile:v', '4444', '-alpha_bits', '16', '-vendor', 'apl0') }
} else {
    $ff += '-framerate', $fps, '-i', (Join-Path $Carpeta 'color_%05d.png')
    $filter = "[0:v]scale=${size}:flags=lanczos" + $(if ($single) { ',format=rgb24[v]' } else { ',format=yuv422p10le[v]' })
    $codec = if ($single) { @('-frames:v', '1') } else { @('-c:v', 'prores_ks', '-profile:v', '3', '-vendor', 'apl0') }
}

$audioArgs = @()
if (-not $single -and $info.audio) {
    $audio = Join-Path (Split-Path (Split-Path $Carpeta -Parent) -Parent) ('audio\' + $info.audio)
    if (Test-Path $audio) {
        $inputs = if ($info.modo -eq 'transparente') { 2 } else { 1 }
        $ff += '-i', $audio
        $audioArgs = @('-map', "${inputs}:a", '-c:a', 'pcm_s16le')
    } else {
        Write-Warning "No se encontró la voz $audio; el .mov sale sin audio."
    }
}

$ff += '-filter_complex', $filter, '-map', '[v]'
$ff += $audioArgs + $codec + @($out)
& $FFmpeg @ff
if ($LASTEXITCODE -ne 0) { throw "FFmpeg terminó con error ($LASTEXITCODE)." }
Write-Host "Listo: $out" -ForegroundColor Green
