@echo off
rem st — 명령줄 도구를 짧게 부른다: st read 대본.yarn --line ln_abc
rem 빌드된 것이 없으면 먼저 빌드한다. 소스를 고쳤거나 받아 왔으면 st-build 를 한 번 부른다.
setlocal
set "DLL=%~dp0src\St.Cli\bin\Release\net10.0\st.dll"
if not exist "%DLL%" dotnet build "%~dp0src\St.Cli\St.Cli.csproj" -c Release --nologo -v quiet 1>&2
dotnet "%DLL%" %*
exit /b %ERRORLEVEL%
