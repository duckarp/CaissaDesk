#!/usr/bin/env bash
set -e

# CaissaDesk Desktop Publisher Script
# Builds standalone, self-contained desktop executables for Windows and Linux.

TARGET=${1:-all}
OUTPUT_DIR="./dist"

echo "=== CaissaDesk Desktop Application Publisher ==="

publish_linux() {
    echo ""
    echo ">> Publishing Linux x64 Desktop Application..."
    dotnet publish CaissaDesk/CaissaDesk.csproj \
        -c Release \
        -r linux-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "${OUTPUT_DIR}/linux-x64"
    echo ">> Linux package created at ${OUTPUT_DIR}/linux-x64/CaissaDesk"
}

publish_windows() {
    echo ""
    echo ">> Publishing Windows x64 Desktop Application..."
    dotnet publish CaissaDesk/CaissaDesk.csproj \
        -c Release \
        -r win-x64 \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "${OUTPUT_DIR}/win-x64"
    echo ">> Windows package created at ${OUTPUT_DIR}/win-x64/CaissaDesk.exe"
}

case "$TARGET" in
    linux)
        publish_linux
        ;;
    win|windows)
        publish_windows
        ;;
    all)
        publish_linux
        publish_windows
        ;;
    *)
        echo "Usage: ./publish.sh [all|linux|windows]"
        exit 1
        ;;
esac

echo ""
echo "=== Publishing completed successfully! ==="
