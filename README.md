# YouTube Downloader

A Windows desktop GUI built on top of [yt-dlp](https://github.com/yt-dlp/yt-dlp) and [FFmpeg](https://ffmpeg.org/). This application does not implement any downloading logic itself — it provides a visual interface for yt-dlp, which does all the heavy lifting.

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple) ![Windows](https://img.shields.io/badge/platform-Windows-blue) ![License](https://img.shields.io/badge/license-MIT-green)

## Features

- YouTube-themed dark UI
- Paste a URL and fetch video info (title, channel, duration, thumbnail)
- Download as **MP4** video with quality selection (Best, 1080p, 720p, 480p, 360p)
- Download as **MP3** audio-only extraction
- Download entire channels/playlists
- Custom filename and save location
- Real-time progress bar and log output
- Auto-updates yt-dlp on startup
- Supports `cookies.txt` for age-restricted videos
- Single portable executable (all tools embedded)

## Requirements

- Windows 10/11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build from source)

## Building from Source

### 1. Download required tools

Run `setup_tools.bat` to automatically download [yt-dlp](https://github.com/yt-dlp/yt-dlp) and [FFmpeg](https://github.com/yt-dlp/FFmpeg-Builds) into the `YouTubeDownloader/Tools/` folder:

```bash
setup_tools.bat
```

Or manually place these files in `YouTubeDownloader/Tools/`:
- `yt-dlp.exe` — from [yt-dlp releases](https://github.com/yt-dlp/yt-dlp/releases)
- `ffmpeg.exe`, `ffprobe.exe`, and FFmpeg DLLs — from [FFmpeg builds](https://github.com/yt-dlp/FFmpeg-Builds/releases)

### 2. Build and publish

```bash
build.bat
```

The output will be a single executable at `dist/YouTubeDownloader.exe`.

On first run, the app extracts the embedded tools to `%LocalAppData%\YouTubeDownloader\tools`. Videos are saved to a `videos` folder next to the executable (or wherever you choose in the UI).

## Cookies (Optional)

For age-restricted or private videos, place a `cookies.txt` file next to the executable. You can export cookies from your browser using extensions like [Get cookies.txt LOCALLY](https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc).

## Credits

This project is a GUI wrapper and would not exist without these tools:

- **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** — The command-line downloader that powers all video/audio downloading and metadata fetching. Licensed under [The Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE).
- **[FFmpeg](https://ffmpeg.org/)** — Used by yt-dlp for video/audio merging and conversion. Licensed under [LGPL/GPL](https://ffmpeg.org/legal.html). Builds from [yt-dlp/FFmpeg-Builds](https://github.com/yt-dlp/FFmpeg-Builds).

## License

[MIT](LICENSE)
