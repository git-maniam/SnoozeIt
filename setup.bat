@echo off
REM SnoozeIt 1-Click Setup Launcher
REM Automatically elevates and bypasses execution policy to run setup.ps1

cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "& '%~dp0setup.ps1'"
pause
