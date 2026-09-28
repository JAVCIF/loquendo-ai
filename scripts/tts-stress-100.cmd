@echo off
setlocal
cd /d "%~dp0.."
set EXE=.\artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe
if not exist "%EXE%" call .\scripts\tts-publish.cmd
if errorlevel 1 exit /b %errorlevel%
"%EXE%" stress --provider loquendo7-native --voice Jorge --count 100 --out-dir .\tts-test-results\stress-100
endlocal
