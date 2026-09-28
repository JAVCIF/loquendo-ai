@echo off
setlocal
cd /d "%~dp0.."

echo Publishing LoquendoAI.TtsBridge32 as self-contained win-x86...
dotnet publish .\src\LoquendoAI.TtsBridge32\LoquendoAI.TtsBridge32.csproj -c Release -r win-x86 --self-contained true -o .\artifacts\ttsbridge-win-x86
if errorlevel 1 exit /b %errorlevel%

echo.
echo Ready: %CD%\artifacts\ttsbridge-win-x86\LoquendoAI.TtsBridge32.exe
endlocal
