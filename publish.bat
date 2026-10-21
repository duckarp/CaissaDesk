@echo off
REM CaissaDesk Desktop Publisher Script for Windows

echo === CaissaDesk Desktop Application Publisher ===

set OUTPUT_DIR=.\dist

echo.
echo ^>^> Publishing Windows x64 Desktop Application...
dotnet publish CaissaDesk\CaissaDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_DIR%\win-x64"

echo.
echo ^>^> Publishing Linux x64 Desktop Application...
dotnet publish CaissaDesk\CaissaDesk.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_DIR%\linux-x64"

echo.
echo === Publishing completed successfully! ===
pause
