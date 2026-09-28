@echo off
setlocal
cd /d "%~dp0.."
call scripts\tts-publish.cmd
if errorlevel 1 exit /b %errorlevel%
dotnet run --project .\src\LoquendoAI.App\LoquendoAI.App.csproj
