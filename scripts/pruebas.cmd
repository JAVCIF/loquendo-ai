@echo off
setlocal
cd /d "%~dp0.."
rem Pruebas automaticas (reparto de tomas, esquema del Director, geometria, SQLite, escaneo, exportacion a VEGAS).
rem Un argumento filtra por nombre: scripts\pruebas.cmd RepositoryTests
dotnet run --project .\tests\LoquendoAI.Tests\LoquendoAI.Tests.csproj -- %*
exit /b %errorlevel%
