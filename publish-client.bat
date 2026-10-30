@echo off
echo Publishing Enova.Client as single-file self-contained executable...
dotnet publish src\Enova.Client\Enova.Client.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts\client
if errorlevel 1 goto :fail

REM Ensure client config sits next to the EXE
if not exist artifacts\client\enova.client.properties (
    copy /Y config\enova.client.properties artifacts\client\enova.client.properties
)
if not exist artifacts\client\config mkdir artifacts\client\config
copy /Y config\enova.client.properties artifacts\client\config\enova.client.properties >nul

echo.
echo Done. Output in artifacts\client\
echo Config is next to Enova.Client.exe
echo.
pause
exit /b 0

:fail
echo Publish failed.
pause
exit /b 1
