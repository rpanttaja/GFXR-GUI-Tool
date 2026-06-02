@echo off
title GFXR Capture Tool
echo Starting GFXR Capture Tool...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0core\run.ps1"
if errorlevel 1 (
    echo.
    echo Launch failed. Review the error above.
    pause
)
