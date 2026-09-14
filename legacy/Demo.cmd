@echo off
rem Klikalny start dema nakladki Claude Status Overlay.
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Demo-ClaudeStatus.ps1" %*
pause
