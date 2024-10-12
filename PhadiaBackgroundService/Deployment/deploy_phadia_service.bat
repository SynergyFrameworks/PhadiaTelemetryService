@echo off
setlocal enabledelayedexpansion

:: Set variables
set SERVICE_NAME=PhadiaTelemetryService
set SERVICE_DISPLAY_NAME=Phadia Telemetry Service
set SERVICE_DESCRIPTION=Processes and transmits allergen telemetry data
set SERVICE_EXECUTABLE=C:\Phadia2500\PhadiaBackgroundService.exe

:: Parse command-line arguments
:parse_args
if "%~1"=="" goto :end_parse_args
if /i "%~1"=="-name" set SERVICE_NAME=%~2
if /i "%~1"=="-display" set SERVICE_DISPLAY_NAME=%~2
if /i "%~1"=="-desc" set SERVICE_DESCRIPTION=%~2
if /i "%~1"=="-exe" set SERVICE_EXECUTABLE=%~2
shift
goto :parse_args
:end_parse_args

:: Check for administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This script requires administrator privileges.
    echo Please run as administrator.
    pause
    exit /b 1
)

:: Stop the service if it's running
echo Stopping the service...
sc stop %SERVICE_NAME%
timeout /t 5 /nobreak > nul

:: Remove the existing service
echo Removing existing service...
sc delete %SERVICE_NAME%
timeout /t 2 /nobreak > nul

:: Install the service
echo Installing the service...
sc create %SERVICE_NAME% binPath= "%SERVICE_EXECUTABLE%" DisplayName= "%SERVICE_DISPLAY_NAME%" start= auto
if %errorLevel% neq 0 (
    echo Failed to create the service.
    pause
    exit /b 1
)

:: Set the description
sc description %SERVICE_NAME% "%SERVICE_DESCRIPTION%"

:: Start the service
echo Starting the service...
sc start %SERVICE_NAME%
if %errorLevel% neq 0 (
    echo Failed to start the service.
    pause
    exit /b 1
)

echo Service deployed successfully.
pause