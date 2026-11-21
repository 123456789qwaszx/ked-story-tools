@echo off
rem st를 다시 빌드한다 — 소스를 고쳤거나 받아 온 뒤에.
dotnet build "%~dp0src\St.Cli\St.Cli.csproj" -c Release --nologo -v quiet
exit /b %ERRORLEVEL%
