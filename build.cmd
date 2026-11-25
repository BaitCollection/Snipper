@echo off
setlocal
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"
set "CSC=%FW%\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework 4 compiler not found.
  pause
  exit /b 1
)
set "ICON="
if exist "%~dp0app.ico" set ICON=/win32icon:"%~dp0app.ico"
set "COMMON=/nologo /target:winexe %ICON% /optimize+ /codepage:65001 /out:"%~dp0Snipper.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll"

rem --- 1st try: with text recognition (OCR) built into Windows 10/11 ---
set "WM=%WINDIR%\System32\WinMetadata"
if not exist "%WM%\Windows.Media.winmd" set "WM=%WINDIR%\Sysnative\WinMetadata"
set OCRREFS=/r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Graphics.winmd" /r:"%WM%\Windows.Storage.winmd" /r:"%WM%\Windows.Security.winmd" /r:"%FW%\System.Runtime.WindowsRuntime.dll" /r:"%FW%\System.Runtime.dll" /r:"%FW%\System.Threading.Tasks.dll"
echo Building Snipper.exe with OCR ...
"%CSC%" %COMMON% /define:OCR %OCRREFS% "%~dp0Snipper.cs" > "%~dp0build_ocr.log" 2>&1
if not errorlevel 1 (
  del "%~dp0build_ocr.log" >nul 2>&1
  goto done
)

rem --- 2nd try: without OCR, so the app still works ---
echo.
echo OCR could not be built on this PC (details: build_ocr.log). Building without OCR ...
"%CSC%" %COMMON% "%~dp0Snipper.cs"
if errorlevel 1 (
  echo.
  echo Build failed. See the errors above.
  pause
  exit /b 1
)
echo Built WITHOUT text recognition. Details are in build_ocr.log.
pause

:done
echo Done. Starting Snipper...
start "" "%~dp0Snipper.exe"
timeout /t 3 >nul
