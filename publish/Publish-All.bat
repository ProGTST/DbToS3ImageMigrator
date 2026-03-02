@echo off
echo BodyImageMigrator - Clean Publish
echo Close Runner first if it is running.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-All.ps1"
set EXIT=%ERRORLEVEL%
echo.
if %EXIT% neq 0 (
  echo Publish failed. Exit code: %EXIT%
) else (
  echo Done.
)
pause
