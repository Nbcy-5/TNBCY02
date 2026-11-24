@echo off
echo ===== TNBCY02 update (v23) =====
set "GODOT_EXE=C:\Users\kolors\AppData\Local\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono_Microsoft.Winget.Source_8wekyb3d8bbwe\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
set "PROJ=C:\TNBCY02"
set "DL=%USERPROFILE%\Downloads"

if not exist "%PROJ%" mkdir "%PROJ%"

rem Newest zip in Downloads whose name starts with TNBCY02 (TNBCY02_v07.zip, TNBCY02_v08.zip ...)
set "NEWZIP="
for /f "delims=" %%f in ('dir /b /o-d "%DL%\TNBCY02*.zip" 2^>nul') do if not defined NEWZIP set "NEWZIP=%%f"
if not defined NEWZIP goto build
echo Using "%NEWZIP%"
tar -xf "%DL%\%NEWZIP%" -C C:\
move /Y "%DL%\%NEWZIP%" "%DL%\done_%NEWZIP%" >nul

:build
if not exist "%PROJ%\project.godot" goto noproject
cd /d "%PROJ%"

for /f %%i in ('powershell -c "Get-Date -Format yyyyMMdd_HHmm"') do set "STAMP=%%i"
if not exist "%PROJ%\Builds" mkdir "%PROJ%\Builds"
set "APK=%PROJ%\Builds\TNBCY02_%STAMP%.apk"

echo Closing leftover Godot / build processes...
taskkill /f /im Godot_v4.7.2-stable_mono_win64_console.exe >nul 2>&1
taskkill /f /im Godot_v4.7.2-stable_mono_win64.exe >nul 2>&1
dotnet build-server shutdown >nul 2>&1

echo.
echo ===== Step 1/3: import  (%time%) =====
"%GODOT_EXE%" --headless --path "%PROJ%" --import --quit
echo ----- step 1 done (%time%)

echo.
echo ===== Step 2/3: build C# code  (%time%) =====
dotnet build "%PROJ%\TNBCY.sln"
if errorlevel 1 goto failed
echo ----- step 2 done (%time%)

echo.
echo ===== Step 3/3: export APK  (%time%) =====
rem Same signing key for PC and GitHub builds (key files live in C:\TNBCY_keys)
if exist "C:\TNBCY_keys\keys.bat" (
  call "C:\TNBCY_keys\keys.bat"
  echo Signing key loaded: C:\TNBCY_keys\tnbcy.keystore
) else (
  echo WARNING: C:\TNBCY_keys\keys.bat not found - APK will use a different key
)
"%GODOT_EXE%" --headless --path "%PROJ%" --export-debug "Android" "%APK%"
if not exist "%APK%" goto failed

echo.
echo APK ready: %APK%
set "ADB=C:\Android\platform-tools\adb.exe"
if exist "%ADB%" "%ADB%" install -r "%APK%"
explorer "%PROJ%\Builds"
pause
exit /b 0

:noproject
echo ERROR: %PROJ%\project.godot not found. Put the TNBCY02 zip in Downloads and run again.
pause
exit /b 1

:failed
echo.
echo FAILED. Copy the last 20 lines above (look for ERROR or error CS) and send them to Claude.
pause
exit /b 1
