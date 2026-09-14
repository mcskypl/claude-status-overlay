@echo off
rem Podglad statusu Claude w sieci lokalnej (telefon).
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\ClaudeStatusServer.ps1" %*
pause
