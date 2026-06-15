# YouTube Downloader

A cross-platform video downloader. On **Windows and Linux** it's a desktop GUI wrapping [yt-dlp](https://github.com/yt-dlp/yt-dlp) and [FFmpeg](https://ffmpeg.org/). On **iPhone/iPad** it's a native app that does YouTube extraction in pure C# (iOS forbids launching yt-dlp/ffmpeg as subprocesses).

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple) ![Windows](https://img.shields.io/badge/Windows-supported-blue) ![Linux](https://img.shields.io/badge/Linux-supported-orange) ![iOS](https://img.shields.io/badge/iOS-sideload-black) ![License](https://img.shields.io/badge/license-MIT-green)

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

Pre-built binaries are on the [Releases](../../releases) page:

- **Windows (x64):** `YouTubeDownloader-windows-x64.exe` — double-click to run.
- **Linux (x64):** `YouTubeDownloader-linux-x64` — `chmod +x` it and run from your file manager or a terminal.
- **iPhone / iPad:** `YouTubeDownloader-ios.ipa` — unsigned, for sideloading (see below).

## Command line (Windows & Linux)

The desktop binary is a hybrid: run it with **no arguments** to open the GUI, or pass **any argument** to use it from the terminal. The Windows build attaches to the calling console automatically.

```
YouTubeDownloader.exe [options] <URL>

ACTIONS
  -d, --download        Download the video (default when a URL is given)
  -i, --info            Print title, channel and duration, then exit
  -h, --help, /?        Show help and exit
  -v, --version         Show the version and exit

FORMAT
  -a, --audio, --mp3    Download audio only, converted to MP3
  -q, --quality <Q>     best, 1080, 720, 480, 360  (default: best)

OUTPUT
  -n, --name <NAME>     Output filename without extension
  -O, --dir <DIR>       Save folder (default: a 'videos' folder next to the exe)

SOURCE
  -u, --url <URL>       URL (or pass it positionally)
  -c, --channel         Download the entire channel / playlist
```

Examples:

```bash
# Best-quality MP4
YouTubeDownloader.exe https://youtu.be/VIDEO

# 1080p with a custom name into a chosen folder
YouTubeDownloader.exe -q 1080 -n "my clip" -O D:\Videos https://youtu.be/VIDEO

# Audio only as MP3
YouTubeDownloader.exe --mp3 https://youtu.be/VIDEO

# Just the metadata
YouTubeDownloader.exe -i https://youtu.be/VIDEO
```

On Linux it's the same flags: `./YouTubeDownloader-linux-x64 --mp3 https://youtu.be/VIDEO`. Windows-style `/flags` (e.g. `/help`, `/q 720`) also work. The CLI is **not** available on iOS.

## iOS (sideloading)

The iOS build is **not on the App Store** and is **unsigned** — you sideload it yourself, which signs it with your own Apple ID. Two common tools:

- **[AltStore](https://altstore.io/)** — install AltServer on a PC/Mac, then install the IPA to your device over Wi-Fi. Free Apple ID works (app must be refreshed every 7 days).
- **[Sideloadly](https://sideloadly.io/)** — plug the device into a PC/Mac, drag in the IPA, sign in with your Apple ID.

### What the iOS app can and can't do

iOS sandboxing forbids apps from launching external programs, so yt-dlp and ffmpeg **cannot run on the device**. Instead the iOS app extracts YouTube streams natively in C# (via [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)). Consequences:

- ✅ Downloads **progressive MP4** (single-file video+audio, typically up to ~720p).
- ✅ Downloads **audio-only M4A** (AAC).
- ❌ No 1080p/4K on iOS — those require merging separate video+audio streams with ffmpeg, which isn't available on-device.
- ❌ No channel/playlist bulk download on iOS (yet).

Saved files land in the app's **Documents/Downloads** folder, accessible from the **Files** app under "YT Downloader", from where you can move them into Photos or share them.

The **desktop** builds keep the full yt-dlp + ffmpeg engine with every quality option.

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

### Building the iOS IPA

The IPA **can only be built on macOS** (it needs Xcode's iOS SDK — there is no Windows path). On a Mac with Xcode and the .NET iOS workload:

```bash
dotnet workload install ios
./build-ios.sh
```

This produces an **unsigned** `dist/YouTubeDownloader-ios.ipa` ready for AltStore/Sideloadly.

Alternatively, push a `v*` tag and the [GitHub Actions workflow](.github/workflows/release.yml) builds all three platforms (Windows + Linux on their runners, the iOS IPA on a macOS runner) and publishes them to a Release automatically.

### Project layout

| Project | Target | Purpose |
|---------|--------|---------|
| `YouTubeDownloader` | `net8.0` (Avalonia) | Windows/Linux desktop GUI (yt-dlp + ffmpeg) |
| `YouTubeDownloader.Core` | `net8.0` | Native C# YouTube extraction (used by iOS) |
| `YouTubeDownloader.iOS` | `net8.0-ios` (Avalonia) | iOS app head, builds the IPA |

## Cookies (Optional)

For age-restricted or private videos, place a `cookies.txt` file next to the binary. You can export cookies from your browser using extensions like [Get cookies.txt LOCALLY](https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc).

## Credits

This project is a GUI wrapper and would not exist without:

- **[yt-dlp](https://github.com/yt-dlp/yt-dlp)** — powers all video/audio downloading. Licensed under [The Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE).
- **[FFmpeg](https://ffmpeg.org/)** — used by yt-dlp for video/audio merging and conversion. Licensed under [LGPL/GPL](https://ffmpeg.org/legal.html). Windows builds from [yt-dlp/FFmpeg-Builds](https://github.com/yt-dlp/FFmpeg-Builds); Linux static builds from [johnvansickle.com](https://johnvansickle.com/ffmpeg/).
- **[Avalonia UI](https://avaloniaui.net/)** — the cross-platform UI framework behind the Linux and iOS builds.
- **[YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)** — pure-C# YouTube extraction that powers the iOS app (no subprocess needed). Licensed under [LGPL-3.0](https://github.com/Tyrrrz/YoutubeExplode/blob/master/License.txt).

## License

[MIT](LICENSE)
