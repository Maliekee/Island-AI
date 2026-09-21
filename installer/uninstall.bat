@echo off
rem Removes the mod only. BepInEx, other mods and all settings stay.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall %*
echo.
pause
