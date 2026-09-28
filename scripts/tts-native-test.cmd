@echo off
setlocal
set ROOT=%~dp0..
set EXE=%ROOT%\artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe
if not exist "%EXE%" (
  echo Bridge no publicado. Ejecuta primero scripts\tts-scan.ps1 o publish-ttsbridge-x86.ps1.
  exit /b 2
)
"%EXE%" synth --provider loquendo7-native --voice Jorge --text "Hola amigos de YouTube, esto es una prueba de Loquendo AI." --out "%ROOT%\jorge_native.wav"
echo.
echo Exit code: %ERRORLEVEL%
if exist "%ROOT%\jorge_native.wav" for %%A in ("%ROOT%\jorge_native.wav") do echo WAV: %%~fA ^(%%~zA bytes^)
endlocal
