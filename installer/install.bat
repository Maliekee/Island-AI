@echo off
rem Runs install.ps1 next to this file. The script is plain text - open it to see what it does.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
echo.
pause
