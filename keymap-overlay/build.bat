@echo off
rem Builds KeymapOverlay.exe using the C# compiler that ships with Windows
rem (.NET Framework 4.x). Nothing to install, no admin needed.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.x
  exit /b 1
)
cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /optimize+ /out:KeymapOverlay.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo Build FAILED.
  if not defined CI pause
  exit /b 1
)
echo Built %~dp0KeymapOverlay.exe
