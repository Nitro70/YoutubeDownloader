@echo off
echo ========================================
echo YouTube Downloader - Build Script
echo ========================================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found!
    echo Please install .NET 8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

call setup_tools.bat
if errorlevel 1 (
    echo Tools setup failed.
    pause
    exit /b 1
)

echo.
echo Publishing Windows single executable...
dotnet publish YouTubeDownloader\YouTubeDownloader.csproj -c Release -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist\win-x64

if errorlevel 1 (
    echo.
    echo ========================================
    echo BUILD FAILED!
    echo ========================================
    pause
    exit /b 1
)

echo.
echo Publishing Linux single binary (cross-compile)...
dotnet publish YouTubeDownloader\YouTubeDownloader.csproj -c Release -r linux-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist\linux-x64

echo.
echo ========================================
echo BUILD SUCCESSFUL!
echo ========================================
echo.
echo Single binaries:
echo   dist\win-x64\YouTubeDownloader.exe
echo   dist\linux-x64\YouTubeDownloader
echo.
echo On first run, each binary extracts its bundled
echo yt-dlp + ffmpeg to:
echo   Windows: %%LocalAppData%%\YouTubeDownloader\tools
echo   Linux:   ~/.local/share/YouTubeDownloader/tools
echo.
echo Videos save to a "videos" folder next to the binary.
echo.
pause
