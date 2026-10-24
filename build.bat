@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. .NET Framework 4.x is required.
    pause
    exit /b 1
)

if not exist "dist" mkdir "dist"

echo [*] Building ...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:"dist\Bandizip747ProPatch.exe" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "src\BandizipProPatch.cs"

if errorlevel 1 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

echo [OK] dist\Bandizip747ProPatch.exe
endlocal
