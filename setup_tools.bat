@echo off
echo ========================================
echo YouTube Downloader - Setup Tools
echo ========================================
echo.

set "WIN_DEST=YouTubeDownloader\Tools\win-x64"
set "LIN_DEST=YouTubeDownloader\Tools\linux-x64"

if not exist "%WIN_DEST%" mkdir "%WIN_DEST%"
if not exist "%LIN_DEST%" mkdir "%LIN_DEST%"

curl --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: curl not found. Please install curl or manually download the tools.
    echo See README.md for manual download links.
    pause
    exit /b 1
)

:: --- Windows tools ---
if not exist "%WIN_DEST%\yt-dlp.exe" (
    echo Downloading Windows yt-dlp...
    curl -L -o "%WIN_DEST%\yt-dlp.exe" "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
) else (
    echo Windows yt-dlp already present.
)

if not exist "%WIN_DEST%\ffmpeg.exe" (
    echo Downloading Windows FFmpeg...
    curl -L -o "%WIN_DEST%\ffmpeg.zip" "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"

    echo Extracting...
    powershell -Command "Expand-Archive -Path '%WIN_DEST%\ffmpeg.zip' -DestinationPath '%WIN_DEST%\ffmpeg-temp' -Force"
    for /d %%D in ("%WIN_DEST%\ffmpeg-temp\ffmpeg-*") do (
        copy "%%D\bin\ffmpeg.exe" "%WIN_DEST%\" >nul
        copy "%%D\bin\ffprobe.exe" "%WIN_DEST%\" >nul
        copy "%%D\bin\*.dll" "%WIN_DEST%\" >nul
    )
    del "%WIN_DEST%\ffmpeg.zip" >nul 2>&1
    rmdir /s /q "%WIN_DEST%\ffmpeg-temp" >nul 2>&1
) else (
    echo Windows FFmpeg already present.
)

:: --- Linux tools ---
if not exist "%LIN_DEST%\yt-dlp" (
    echo Downloading Linux yt-dlp...
    curl -L -o "%LIN_DEST%\yt-dlp" "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux"
) else (
    echo Linux yt-dlp already present.
)

if not exist "%LIN_DEST%\ffmpeg" (
    echo Downloading Linux static FFmpeg...
    curl -L -o "%LIN_DEST%\ffmpeg.tar.xz" "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz"
    echo Extracting...
    tar -xf "%LIN_DEST%\ffmpeg.tar.xz" -C "%LIN_DEST%"
    for /d %%D in ("%LIN_DEST%\ffmpeg-*-amd64-static") do (
        copy "%%D\ffmpeg" "%LIN_DEST%\" >nul
        copy "%%D\ffprobe" "%LIN_DEST%\" >nul
        rmdir /s /q "%%D"
    )
    del "%LIN_DEST%\ffmpeg.tar.xz" >nul 2>&1
) else (
    echo Linux FFmpeg already present.
)

echo.
echo ========================================
echo Tools setup complete!
echo ========================================
echo.
