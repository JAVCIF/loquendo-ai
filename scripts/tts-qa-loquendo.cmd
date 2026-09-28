@echo off
setlocal
cd /d "%~dp0.."
set EXE=.\artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe
if not exist "%EXE%" call .\scripts\tts-publish.cmd
if errorlevel 1 exit /b %errorlevel%
"%EXE%" qa-suite --provider loquendo7-native --voice Jorge --out-dir .\tts-test-results\qa-loquendo
endlocal
