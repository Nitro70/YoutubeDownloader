#!/usr/bin/env bash
set -euo pipefail

echo "========================================"
echo "YouTube Downloader - Setup Tools"
echo "========================================"
echo

WIN_DEST="YouTubeDownloader/Tools/win-x64"
LIN_DEST="YouTubeDownloader/Tools/linux-x64"
mkdir -p "$WIN_DEST" "$LIN_DEST"

if ! command -v curl >/dev/null 2>&1; then
    echo "ERROR: curl not found. Install curl and retry."
    exit 1
fi

# --- Windows tools ---
if [[ ! -f "$WIN_DEST/yt-dlp.exe" ]]; then
    echo "Downloading Windows yt-dlp..."
    curl -L -o "$WIN_DEST/yt-dlp.exe" \
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
else
    echo "Windows yt-dlp already present."
fi

if [[ ! -f "$WIN_DEST/ffmpeg.exe" ]]; then
    echo "Downloading Windows FFmpeg..."
    curl -L -o "$WIN_DEST/ffmpeg.zip" \
        "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"
    mkdir -p "$WIN_DEST/ffmpeg-temp"
    unzip -q -o "$WIN_DEST/ffmpeg.zip" -d "$WIN_DEST/ffmpeg-temp"
    for d in "$WIN_DEST/ffmpeg-temp"/ffmpeg-*; do
        cp "$d/bin/"ffmpeg.exe "$WIN_DEST/"
        cp "$d/bin/"ffprobe.exe "$WIN_DEST/"
        cp "$d/bin/"*.dll "$WIN_DEST/"
    done
    rm -rf "$WIN_DEST/ffmpeg-temp" "$WIN_DEST/ffmpeg.zip"
else
    echo "Windows FFmpeg already present."
fi

# --- Linux tools ---
if [[ ! -f "$LIN_DEST/yt-dlp" ]]; then
    echo "Downloading Linux yt-dlp..."
    curl -L -o "$LIN_DEST/yt-dlp" \
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux"
else
    echo "Linux yt-dlp already present."
fi

if [[ ! -f "$LIN_DEST/ffmpeg" ]]; then
    echo "Downloading Linux static FFmpeg..."
    curl -fL -o "$LIN_DEST/ffmpeg.tar.xz" \
        "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz"
    tar -xf "$LIN_DEST/ffmpeg.tar.xz" -C "$LIN_DEST"
    for d in "$LIN_DEST"/ffmpeg-*-linux64-gpl; do
        cp "$d/bin/ffmpeg" "$d/bin/ffprobe" "$LIN_DEST/"
        rm -rf "$d"
    done
    rm -f "$LIN_DEST/ffmpeg.tar.xz"
else
    echo "Linux FFmpeg already present."
fi

echo
echo "========================================"
echo "Tools setup complete!"
echo "========================================"
