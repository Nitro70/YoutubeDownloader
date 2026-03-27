@echo off
echo ========================================
echo YouTube Downloader - Setup Tools
echo ========================================
echo.

set "DEST=YouTubeDownloader\Tools"

:: Create Tools directory
if not exist "%DEST%" mkdir "%DEST%"

:: Check for curl
curl --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: curl not found. Please install curl or manually download the tools.
    echo See README.md for manual download links.
    pause
    exit /b 1
)

:: Download yt-dlp
if not exist "%DEST%\yt-dlp.exe" (
    echo Downloading yt-dlp.exe...
    curl -L -o "%DEST%\yt-dlp.exe" "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe"
) else (
    echo yt-dlp.exe already exists, skipping.
)

:: Download FFmpeg (yt-dlp's recommended build)
if not exist "%DEST%\ffmpeg.exe" (
    echo Downloading FFmpeg...
    echo This may take a moment - FFmpeg is a large download.
    curl -L -o "%DEST%\ffmpeg.zip" "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip"

    echo Extracting FFmpeg...
    powershell -Command "Expand-Archive -Path '%DEST%\ffmpeg.zip' -DestinationPath '%DEST%\ffmpeg-temp' -Force"

    :: Copy files from the nested bin directory
    for /d %%D in ("%DEST%\ffmpeg-temp\ffmpeg-*") do (
        copy "%%D\bin\ffmpeg.exe" "%DEST%\" >nul
        copy "%%D\bin\ffprobe.exe" "%DEST%\" >nul
        copy "%%D\bin\ffplay.exe" "%DEST%\" >nul
        copy "%%D\bin\*.dll" "%DEST%\" >nul
    )

    :: Cleanup
    del "%DEST%\ffmpeg.zip" >nul 2>&1
    rmdir /s /q "%DEST%\ffmpeg-temp" >nul 2>&1
) else (
    echo FFmpeg already exists, skipping.
)

echo.
echo ========================================
echo Tools setup complete!
echo ========================================
echo.
echo Files downloaded to: %DEST%
echo.
echo NOTE: If you need cookies for age-restricted videos,
echo manually place cookies.txt next to YouTubeDownloader.exe
echo.
pause
