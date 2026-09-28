@echo off
setlocal
set "ROOT=%~dp0.."
set "BRIDGE=%ROOT%\artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe"
if not exist "%BRIDGE%" call "%~dp0tts-publish.cmd"
if not exist "%BRIDGE%" exit /b 1
set "VOICE=%~1"
if "%VOICE%"=="" set "VOICE=Antonio (Spanish) SAPI4 22kHz"
"%BRIDGE%" balcon-probe --voice "%VOICE%"
endlocal
