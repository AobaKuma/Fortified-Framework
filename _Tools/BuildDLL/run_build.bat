@echo off
REM ---------------------------------------------------------------------------
REM  FFF local assembly builder launcher (Fortified Feature Framework)
REM  Double-click to build every C# project of this mod and its sibling mods,
REM  in dependency order (Fortified before FortifiedCE).
REM  Optional: drag a .csproj or a mod folder onto this .bat to build only that.
REM  Extra flags are forwarded, e.g.  run_build.bat --list
REM ---------------------------------------------------------------------------
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "SCRIPT=%~dp0build_dlls.py"
set "REPORT=%~dp0BuildReport.txt"

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

REM dotnet is checked again inside the script; this is just a friendlier message.
where dotnet >nul 2>nul || (
    echo.
    echo WARNING: 'dotnet' is not on PATH. If the build fails immediately,
    echo install the .NET SDK from https://dotnet.microsoft.com/download
    echo.
)

echo Building with "%PY%"...
echo.
"%PY%" "%SCRIPT%" %*
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
    echo Build succeeded.
) else (
    echo Build finished with exit code %RC%.
    if exist "%REPORT%" start "" "%REPORT%"
)

echo.
pause
endlocal
