@echo off
setlocal
cd /d "%~dp0.."
rem Version final: LoquendoAI.exe, carpeta portable y zip en artifacts\. Opciones: -Pruebas -Instalador -DependeDeNet
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\publicar.ps1" %*
if errorlevel 1 (
  echo.
  echo [ERROR] La publicacion fallo. Revisa el mensaje de arriba.
  pause
  exit /b 1
)
endlocal
