@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4 compiler not found.
  pause
  exit /b 1
)
echo Building Snipper.exe ...
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%~dp0Snipper.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0Snipper.cs"
if errorlevel 1 (
  echo.
  echo Build failed. See the errors above.
  pause
  exit /b 1
)
echo Done. Starting Snipper...
start "" "%~dp0Snipper.exe"
timeout /t 3 >nul
