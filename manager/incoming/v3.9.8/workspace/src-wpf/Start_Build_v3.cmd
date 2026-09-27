@echo off
setlocal
cd /d "%~dp0"
echo FreeCam Manager v3.3 WPF - one-click local build
echo.
echo This will use an existing .NET 8 SDK, or download a portable official Microsoft SDK locally if needed.
echo No administrator rights are required.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build_v3_On_Windows.ps1"
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Please send BuildOutput\BUILD_RESULT.txt back to ChatGPT.
  pause
  exit /b 1
)
echo.
echo BUILD OK. BuildOutput\FreeCam_Manager_v3.3.exe is ready.
pause
