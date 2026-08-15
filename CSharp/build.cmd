@echo off
setlocal
cd /d "%~dp0"
dotnet build StarRunnerPrototype.csproj -c Release
pause
