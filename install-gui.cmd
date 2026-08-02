@echo off
setlocal

set "INSTALL_SCRIPT=%~dp0install.ps1"
if not exist "%INSTALL_SCRIPT%" set "INSTALL_SCRIPT=%~dp0scripts\install-gui.ps1"

if not exist "%INSTALL_SCRIPT%" (
    echo LightYTP GUI installer files are incomplete.
    echo Download the LightYTP GUI Windows release ZIP, not GitHub's Source code ZIP.
    pause
    exit /b 2
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%INSTALL_SCRIPT%" %*
if errorlevel 1 (
    echo.
    echo LightYTP GUI installation failed.
    pause
    exit /b 1
)

echo.
echo Installation complete. Open LightYTP GUI from the Start menu.
pause
