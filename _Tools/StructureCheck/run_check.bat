@echo off
REM ---------------------------------------------------------------------------
REM  FFF structure-layout overlap checker launcher (Fortified Feature Framework)
REM  Double-click to scan this mod plus its sibling mod folders for
REM  power-transmitter and edifice overlaps in FFF_StructureDef layouts.
REM  Optional: drag a mod folder onto this .bat to scan only that folder.
REM ---------------------------------------------------------------------------
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "SCRIPT=%~dp0check_structures.py"

REM Locate a Python launcher: prefer the "py" launcher, then "python".
set "PY="
where py >nul 2>nul && set "PY=py"
if not defined PY (
    where python >nul 2>nul && set "PY=python"
)

if not defined PY (
    echo.
    echo ERROR: Python was not found on this system.
    echo Install Python 3 from https://www.python.org/ and re-run.
    echo.
    pause
    exit /b 1
)

echo Running structure overlap check with "%PY%"...
echo.
"%PY%" "%SCRIPT%" %*
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
    if exist "%~dp0StructureOverlaps.txt" start "" "%~dp0StructureOverlaps.txt"
) else (
    echo Script exited with code %RC%.
)

echo.
pause
endlocal
