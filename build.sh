#!/usr/bin/env bash
set -euo pipefail

echo "========================================"
echo "YouTube Downloader - Build Script"
echo "========================================"
echo

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: .NET SDK not found."
    echo "Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
fi

./setup_tools.sh

PUBLISH_FLAGS=(
    -c Release
    --self-contained true
    -p:PublishSingleFile=true
    -p:EnableCompressionInSingleFile=true
    -p:IncludeNativeLibrariesForSelfExtract=true
)

echo
echo "Publishing Linux single binary..."
dotnet publish YouTubeDownloader/YouTubeDownloader.csproj "${PUBLISH_FLAGS[@]}" \
    -r linux-x64 -o dist/linux-x64

echo
echo "Publishing Windows single executable (cross-compile)..."
dotnet publish YouTubeDownloader/YouTubeDownloader.csproj "${PUBLISH_FLAGS[@]}" \
    -r win-x64 -o dist/win-x64

chmod +x dist/linux-x64/YouTubeDownloader

echo
echo "========================================"
echo "BUILD SUCCESSFUL!"
echo "========================================"
echo
echo "Binaries:"
echo "  dist/linux-x64/YouTubeDownloader"
echo "  dist/win-x64/YouTubeDownloader.exe"
echo
echo "On first run each binary extracts its bundled"
echo "yt-dlp + ffmpeg to:"
echo "  Linux:   ~/.local/share/YouTubeDownloader/tools"
echo "  Windows: %LocalAppData%\\YouTubeDownloader\\tools"
echo
echo "Videos save to a 'videos' folder next to the binary."
