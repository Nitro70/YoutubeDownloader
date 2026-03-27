@echo off
echo ========================================
echo YouTube Downloader - Build Script
echo ========================================
echo.

:: Check for dotnet
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found!
    echo Please install .NET 8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:: Setup tools first
call setup_tools.bat

:: Restore packages
echo.
echo Restoring NuGet packages...
dotnet restore YouTubeDownloader.sln

:: Build Release
echo.
echo Building Release...
dotnet build YouTubeDownloader.sln -c Release

if errorlevel 1 (
    echo.
    echo ========================================
    echo BUILD FAILED!
    echo ========================================
    pause
    exit /b 1
)

:: Publish as single file
echo.
echo Publishing single executable...
dotnet publish YouTubeDownloader\YouTubeDownloader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist

if errorlevel 1 (
    echo.
    echo ========================================
    echo PUBLISH FAILED!
    echo ========================================
    pause
    exit /b 1
)

echo.
echo ========================================
echo BUILD SUCCESSFUL!
echo ========================================
echo.
echo Single executable created at:
echo   dist\YouTubeDownloader.exe
echo.
echo This single .exe contains everything!
echo On first run, it extracts tools to:
echo   %%LocalAppData%%\YouTubeDownloader\tools
echo.
echo Videos will be saved to a "videos" folder
echo next to wherever you put the .exe
echo.
pause
