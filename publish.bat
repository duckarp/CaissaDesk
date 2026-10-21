@echo off
setlocal
REM CaissaDesk Desktop Publisher Script for Windows
REM Usage: publish.bat [all^|windows^|linux]   (default: all)

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=all"
set "OUTPUT_DIR=.\dist"

REM Pause at the end only when launched by double-click from Explorer (cmd /c "...publish.bat").
set "INTERACTIVE="
echo %CMDCMDLINE% | find /i "%~0" >nul && set "INTERACTIVE=1"

echo === CaissaDesk Desktop Application Publisher ===

if /i "%TARGET%"=="all" goto :all
if /i "%TARGET%"=="windows" goto :windows
if /i "%TARGET%"=="win" goto :windows
if /i "%TARGET%"=="linux" goto :linux
echo Usage: publish.bat [all^|windows^|linux]
set "RC=1"
goto :end

:all
call :publish_windows || goto :fail
call :publish_linux || goto :fail
goto :success

:windows
call :publish_windows || goto :fail
goto :success

:linux
call :publish_linux || goto :fail
goto :success

:publish_windows
echo.
echo ^>^> Publishing Windows x64 Desktop Application...
dotnet publish CaissaDesk\CaissaDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_DIR%\win-x64"
if errorlevel 1 exit /b 1
echo ^>^> Windows package created at %OUTPUT_DIR%\win-x64\CaissaDesk.exe
exit /b 0

:publish_linux
echo.
echo ^>^> Publishing Linux x64 Desktop Application...
dotnet publish CaissaDesk\CaissaDesk.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_DIR%\linux-x64"
if errorlevel 1 exit /b 1
echo ^>^> Linux package created at %OUTPUT_DIR%\linux-x64\CaissaDesk
exit /b 0

:fail
echo.
echo === Publishing FAILED ===
set "RC=1"
goto :end

:success
echo.
echo === Publishing completed successfully! ===
set "RC=0"

:end
if defined INTERACTIVE pause
endlocal & exit /b %RC%
