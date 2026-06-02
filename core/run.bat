@echo off
title GFXR Capture Tool
echo Starting GFXR Capture Tool bootstrap...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1"
if errorlevel 1 (
    echo.
    echo Bootstrap failed. Review the error above.
    pause
)
