@echo off
setlocal
cd /d "%~dp0"

echo Suljetaan mahdollinen vanha Abitti2Dashboard.exe...
taskkill /IM Abitti2Dashboard.exe /F >nul 2>nul
taskkill /IM AbittiHallinta.exe /F >nul 2>nul

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
  echo.
  echo Windowsin .NET Framework C# -kaantajaa ei loytynyt.
  echo Tarvitset .NET Framework 4.x:n tai Visual Studion Build Toolsin.
  pause
  exit /b 1
)

if not exist "ui.html" (
  echo.
  echo ui.html puuttuu. Lataa repository kokonaisena.
  pause
  exit /b 1
)

echo Rakennetaan Abitti2Dashboard.exe...
"%CSC%" /nologo /target:winexe /out:Abitti2Dashboard.exe ^
 /resource:ui.html,AbittiHallinta.ui.html ^
 /reference:System.dll ^
 /reference:System.Core.dll ^
 /reference:System.Web.Extensions.dll ^
 /reference:System.Security.dll ^
 /reference:System.Windows.Forms.dll ^
 src\Program.Core.cs ^
 src\Program.Http.cs ^
 src\Program.WebSocket.cs ^
 src\Program.Storage.cs

if errorlevel 1 (
  echo.
  echo Kaannos epaonnistui.
  pause
  exit /b 1
)

echo.
echo Valmis. Kaynnistetaan Abitti2Dashboard.exe...
start "" "%CD%\Abitti2Dashboard.exe"
endlocal
