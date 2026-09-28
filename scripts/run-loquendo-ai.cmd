@echo off
setlocal
cd /d "%~dp0.."
if exist "worker\python\python.exe" goto stt_ready
if exist "worker\.venv\Scripts\python.exe" (
    "worker\.venv\Scripts\python.exe" -c "import faster_whisper" >nul 2>nul
    if not errorlevel 1 goto stt_ready
)
echo Preparando transcripcion local (solo si falta instalarla)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "scripts\stt-setup.ps1"
if errorlevel 1 (
    echo [AVISO] STT no disponible. Puedes seguir usando el editor y ejecutar scripts\stt-setup.ps1 mas tarde.
)
:stt_ready
call scripts\tts-publish.cmd
if errorlevel 1 exit /b %errorlevel%
dotnet build .\LoquendoAI.sln
if errorlevel 1 exit /b %errorlevel%
dotnet run --project .\src\LoquendoAI.App\LoquendoAI.App.csproj
