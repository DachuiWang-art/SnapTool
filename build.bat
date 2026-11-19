@echo off
setlocal

rem Build SnapTool.exe with the csc compiler that ships with Windows.
rem No Visual Studio or .NET SDK required.

set CSC64=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set CSC32=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
set CSC=%CSC64%
if not exist "%CSC%" set CSC=%CSC32%

if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
  pause
  exit /b 1
)

echo Compiling SnapTool.exe ...
"%CSC%" /nologo /target:winexe /optimize /win32icon:SnapTool.ico /out:SnapTool.exe SnapTool.cs /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll

if errorlevel 1 (
  echo [ERROR] Compile failed.
  pause
  exit /b 1
)

echo Done: SnapTool.exe
pause
