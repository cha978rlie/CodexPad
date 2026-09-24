@echo off
setlocal
if not exist "%~dp0CodexPad.exe" (
    echo CodexPad.exe fehlt. Bitte den vollstaendigen Programmordner wiederherstellen.
    pause
    exit /b 1
)
start "" "%~dp0CodexPad.exe" --pad-test
