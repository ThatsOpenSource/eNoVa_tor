@echo off
setlocal

echo ========================================
echo eNoVA EGOV – Setup
echo ========================================
echo.

REM --- Check for Docker ---
where docker >nul 2>&1
if %ERRORLEVEL% equ 0 (
    docker --version >nul 2>&1
    if %ERRORLEVEL% equ 0 (
        echo Docker is installed.
        goto :start_db
    )
)

echo Docker is not installed or not available in PATH.
echo.
echo The database requires Docker Desktop.
echo.
choice /C YN /M "Open the Docker Desktop download page now"
if %ERRORLEVEL% equ 1 (
    start https://www.docker.com/products/docker-desktop/
    echo.
    echo After installing Docker Desktop:
    echo   1. Start Docker Desktop and wait until it is running
    echo   2. Re-run this script
    echo.
    pause
    exit /b 1
)

echo.
echo You can also install via winget (Windows 11):
echo   winget install -e --id Docker.DockerDesktop
echo.
pause
exit /b 1

:start_db
echo.
echo Starting PostgreSQL via Docker Compose...
cd /d "%~dp0docker"
docker compose up -d
if %ERRORLEVEL% neq 0 (
    echo Failed to start the database. Is Docker Desktop running?
    pause
    exit /b 1
)

echo.
echo Database is starting on localhost:55432
echo   User:     enova
echo   Password: 1234
echo.
echo Next steps:
echo   1. Start the server:  dotnet run --project src\Enova.Server
echo      or run the published Enova.Server.exe
echo   2. Start the client:  dotnet run --project src\Enova.Client
echo      or run the published Enova.Client.exe
echo.
pause
endlocal
