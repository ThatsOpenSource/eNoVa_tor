@echo off
echo Publishing Enova.Server as single-file self-contained executable...
dotnet publish src\Enova.Server\Enova.Server.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts\server
if errorlevel 1 goto :fail

REM Copy server config next to the server EXE
if not exist artifacts\server\config mkdir artifacts\server\config
copy /Y config\enova.server.properties artifacts\server\enova.server.properties >nul
copy /Y config\enova.server.properties artifacts\server\config\enova.server.properties >nul

echo.
echo Done. Output in artifacts\server\
echo.
pause
exit /b 0

:fail
echo Publish failed.
pause
exit /b 1
