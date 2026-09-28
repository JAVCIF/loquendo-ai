@echo off
setlocal
set EXE=%~dp0..\artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe
if not exist "%EXE%" (
  echo Bridge not published. Run scripts\tts-scan.ps1 once, or publish win-x86 self-contained.
  exit /b 2
)
"%EXE%" abi-probe --voice Jorge
endlocal
