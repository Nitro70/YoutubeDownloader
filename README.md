# YouTube Downloader

A cross-platform (Windows + Linux) desktop GUI built on top of [yt-dlp](https://github.com/yt-dlp/yt-dlp) and [FFmpeg](https://ffmpeg.org/). The app does not implement any downloading logic itself — it's a visual front-end for yt-dlp, which does all the heavy lifting.

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple) ![Windows](https://img.shields.io/badge/Windows-supported-blue) ![Linux](https://img.shields.io/badge/Linux-supported-orange) ![License](https://img.shields.io/badge/license-MIT-green)

## Features

- YouTube-themed dark UI built with [Avalonia](https://avaloniaui.net/)
- Paste a URL and fetch video info (title, channel, duration, thumbnail)
- Download as **MP4** video with quality selection (Best, 1080p, 720p, 480p, 360p)
- Download as **MP3** audio-only extraction
- Download entire channels/playlists
- Custom filename and save location
- Live progress bar showing size, speed, and ETA
- Auto-updates yt-dlp on startup
- Supports `cookies.txt` for age-restricted videos
- **Standalone portable binaries** — the single executable bundles yt-dlp, FFmpeg, and the .NET runtime. No installation or external dependencies required to run it.

## Download

Pre-built standalone binaries are available on the [Releases](../../releases) page:

- **Windows (x64):** `YouTubeDownloader.exe` — double-click to run.
- **Linux (x64):** `YouTubeDownloader` — `chmod +x` it and run from your file manager or a terminal.

## Requirements (Building from Source)

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- `curl` and `unzip`/`tar` to pull bundled tool binaries
- Windows or Linux (you can cross-compile both targets from either host)

## Building from Source

The bundled tool binaries (yt-dlp, FFmpeg) are too large for git and are downloaded during setup. They get embedded into the final executable — the result is completely standalone.

### Windows host

```bash
setup_tools.bat
build.bat
```

Produces:
- `dist\win-x64\YouTubeDownloader.exe`
- `dist\linux-x64\YouTubeDownloader` (cross-compiled Linux binary)

### Linux host

```bash
./setup_tools.sh
./build.sh
```

Produces the same two binaries.

On first run, the app extracts the embedded tools to:
- Windows: `%LocalAppData%\YouTubeDownloader\tools`
- Linux: `~/.local/share/YouTubeDownloader/tools`

Videos save to a `videos` folder next to the binary (or wherever you choose in the UI).

## Cookies (Optional)

For age-restricted or private videos, place a `cookies.txt` file next to the binary. You can export cookies from your browser using extensions like [Get cookies.txt LOCALLY](https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc).

## Credits

This project is a GUI wrapper and would not exist without:

- **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** — powers all video/audio downloading. Licensed under [The Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE).
- **[FFmpeg](https://ffmpeg.org/)** — used by yt-dlp for video/audio merging and conversion. Licensed under [LGPL/GPL](https://ffmpeg.org/legal.html). Windows builds from [yt-dlp/FFmpeg-Builds](https://github.com/yt-dlp/FFmpeg-Builds); Linux static builds from [johnvansickle.com](https://johnvansickle.com/ffmpeg/).
- **[Avalonia UI](https://avaloniaui.net/)** — the cross-platform UI framework that makes the Linux build possible.

## License

[MIT](LICENSE)
