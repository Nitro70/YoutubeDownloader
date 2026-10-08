# YouTube Downloader

A cross-platform video downloader. On **Windows and Linux** it's a desktop GUI (and CLI) wrapping [yt-dlp](https://github.com/yt-dlp/yt-dlp) and [FFmpeg](https://ffmpeg.org/). On **Android and iPhone/iPad** it's a native app that does YouTube extraction in pure C# (mobile OSes can't run yt-dlp/ffmpeg as subprocesses). Every version can **search YouTube**, so you don't need a link.

![.NET 10](https://img.shields.io/badge/.NET-10.0-purple) ![Windows](https://img.shields.io/badge/Windows-supported-blue) ![Linux](https://img.shields.io/badge/Linux-supported-orange) ![Android](https://img.shields.io/badge/Android-sideload-green) ![iOS](https://img.shields.io/badge/iOS-sideload-black) ![License](https://img.shields.io/badge/license-MIT-green)

## Features

- YouTube-themed dark UI built with [Avalonia](https://avaloniaui.net/)
- **Search YouTube by typing words** in the link box: pick from the top 10 results, or hit Download to grab the first one
- Paste a URL and fetch video info (title, channel, duration, thumbnail), with a **Copy link** button
- Download as **MP4** video with quality selection (Best, 1080p, 720p, 480p, 360p)
- Download as **MP3** audio-only extraction
- Download entire channels/playlists
- Custom filename and save location
- Live progress bar showing size, speed, and ETA
- Auto-updates yt-dlp on startup
- Supports `cookies.txt` for age-restricted videos
- **Standalone portable binaries:** the single executable bundles yt-dlp, FFmpeg, and the .NET runtime. Nothing to install.

## Download

Pre-built binaries are on the [Releases](../../releases) page:

- **Windows (x64):** `YouTubeDownloader-windows-x64.exe`. Double-click to run.
- **Linux (x64):** `YouTubeDownloader-linux-x64`. `chmod +x` it and run it from your file manager or a terminal.
- **Android:** `YouTubeDownloader-android.apk`. Self-signed, for sideloading (see below).
- **iPhone / iPad:** `YouTubeDownloader-ios.ipa`. Unsigned, for sideloading (see below).

## Searching

Type words instead of a link and the box turns into a search:

- **Press Enter** (or the **Search** button) to list the top 10 videos. Click or tap one to fill in its link and load its info.
- **Press Download** with words still in the box to download the first result straight away.
- Anything that looks like a link (`https://...`, or a bare `youtube.com/...` / `youtu.be/...`) is treated as a link, everything else as search words.

Results are plain videos only: channels, playlists, shelves and ads are skipped.

## Command line (Windows & Linux)

The desktop binary is a hybrid: run it with **no arguments** to open the GUI, or pass **any argument** to use it from the terminal. The Windows build attaches to the calling console automatically.

```
YouTubeDownloader.exe [options] <URL>
YouTubeDownloader.exe [options] <search words>

ACTIONS
  -d, --download        Download the video (default when a URL is given)
  -i, --info            Print title, channel and duration, then exit
  -s, --search          Print the link of the first video found for the
                        search words (Windows: also copied to the clipboard)
  -r, --results <N>     With --search, list the top N results (1-50)
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
  <search words>        Anything that isn't a link is a YouTube search;
                        the first video found is used
  -c, --channel         Download the entire channel / playlist
  --                    Everything after this is search words, even if it
                        starts with - or /
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

# Find a video: prints the first result's link and copies it
YouTubeDownloader.exe -s never gonna give you up

# List the top 5 results with titles
YouTubeDownloader.exe -s -r 5 lofi hip hop

# Search and download the first result as MP3, no link needed
YouTubeDownloader.exe --mp3 never gonna give you up
```

On Linux it's the same flags: `./YouTubeDownloader-linux-x64 --mp3 never gonna give you up`. Windows-style `/flags` (e.g. `/help`, `/q 720`) also work. The CLI is **not** available on the phone apps.

## Android (sideloading)

The Android build is **not on the Play Store**. It's a self-signed APK you install directly:

1. Download `YouTubeDownloader-android.apk` to the phone.
2. Open it. Android will ask you to allow installing from this source: enable **"install unknown apps"** for your browser or file manager.
3. Install and open.

Needs Android 7.0 or newer (ARM phones, 32 or 64-bit).

Saved files land in `Android/data/com.nitro70.youtubedownloader/files/Downloads`, reachable from a file manager.

> The APK is signed with a throwaway key generated at build time, so a new release may not install *over* an older one. Uninstall the old version first if Android refuses the update.

## iOS (sideloading)

The iOS build is **not on the App Store** and is **unsigned**: you sideload it yourself, which signs it with your own Apple ID. Two common tools:

- **[AltStore](https://altstore.io/):** install AltServer on a PC/Mac, then install the IPA to your device over Wi-Fi. A free Apple ID works (the app must be refreshed every 7 days).
- **[Sideloadly](https://sideloadly.io/):** plug the device into a PC/Mac, drag in the IPA, sign in with your Apple ID.

Videos go straight into the **Photos** app (the **Save videos to Photos** switch, on by default; iOS asks once for permission to add photos). Audio files, and videos when the switch is off, land in the app's **Documents/Downloads** folder, visible in the **Files** app under "YT Downloader".

**[LiveContainer](https://github.com/LiveContainer/LiveContainer)** works too. Saving to Photos works in its normal mode. In its multitasking mode the switch is hidden, because LiveContainer's multitasking process has no photo permission and iOS would close the app if it asked; files are then kept inside LiveContainer's data.

## What the phone apps can and can't do

Phones can't run yt-dlp or ffmpeg, so the Android and iOS apps extract YouTube streams natively in C# (via [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)). That means:

- ✅ **Search** works the same as on desktop.
- ✅ **Video** downloads as MP4, up to **1080p**. YouTube serves video and audio as separate streams; the app downloads an H.264 video stream and an AAC audio stream and merges them into one MP4 on the phone, with its own small MP4 muxer (no ffmpeg, no re-encoding). H.264 is used because it plays in iPhone Photos and Android galleries, unlike YouTube's VP9/AV1 streams.
- ✅ **Audio-only M4A** (AAC) downloads.
- ✅ **iPhone: videos saved straight to Photos** (can be switched off).
- ❌ No 1440p/4K on the phone apps: YouTube only offers those as VP9/AV1, not H.264.
- ❌ No channel/playlist bulk download on the phone apps.

The **desktop** builds keep the full yt-dlp + ffmpeg engine with every quality option.

## Requirements (Building from Source)

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- `curl` and `unzip`/`tar` to pull bundled tool binaries
- Windows or Linux (you can cross-compile both targets from either host)

## Building from Source

The bundled tool binaries (yt-dlp, FFmpeg) are too large for git and are downloaded during setup. They get embedded into the final executable, so the result is completely standalone.

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

The IPA **can only be built on macOS** (it needs Xcode's iOS SDK; there is no Windows path). On a Mac with **Xcode 26 or newer**, the **.NET 10 SDK** and the .NET iOS workload:

```bash
dotnet workload install ios
./build-ios.sh
```

This produces an **unsigned** `dist/YouTubeDownloader-ios.ipa` ready for AltStore/Sideloadly. The app targets the iOS 26.0 SDK, which .NET pairs with Xcode 26.0; with a newer Xcode (tested with 27.0) the script builds anyway and says so. If a rebuild crashes at launch with "Failed to load AOT module", delete `YouTubeDownloader.iOS/bin` and `obj` and build again: the iOS SDK's incremental build can leave stale precompiled code. No Mac? Use the CI route below.

### Building the Android APK

Builds on any OS with the **.NET 10 SDK**, the Android workload, and an Android SDK (API 36 + build-tools 36) + JDK:

```bash
dotnet workload install android
./build-android.sh
```

This produces a self-signed `dist/YouTubeDownloader-android.apk`. It takes about half a minute.

### Everything at once, via CI

Push a `v*` tag (or run the workflow by hand from the Actions tab) and the [GitHub Actions workflow](.github/workflows/release.yml) builds **all four** platforms: Windows, Linux and the Android APK on their runners, the iOS IPA on a macOS runner. On a tag it also publishes them to a Release (a pre-release for tags like `v4.0.0-beta.1`). The Android job checks that the APK really contains the app and its launcher before uploading it. A full run takes about 10 minutes; the iOS job is the longest.

### Project layout

| Project | Target | Purpose |
|---------|--------|---------|
| `YouTubeDownloader` | `net10.0` (Avalonia 12) | Windows/Linux desktop GUI + CLI (yt-dlp + ffmpeg, search via Core) |
| `YouTubeDownloader.Core` | `net10.0` | Native C# YouTube search, extraction and MP4 merging (shared) |
| `YouTubeDownloader.iOS` | `net10.0-ios26.0` (Avalonia 12) | iOS app head, builds the IPA |
| `YouTubeDownloader.Android` | `net10.0-android` (Avalonia 12) | Android app head, builds the APK |

## Cookies (Optional)

For age-restricted or private videos, place a `cookies.txt` file next to the binary. You can export cookies from your browser using extensions like [Get cookies.txt LOCALLY](https://chromewebstore.google.com/detail/get-cookiestxt-locally/cclelndahbckbenkjhflpdbgdldlbecc).

## Credits

This project is a GUI wrapper and would not exist without:

- **[yt-dlp](https://github.com/yt-dlp/yt-dlp):** powers all desktop video/audio downloading. Licensed under [The Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE).
- **[FFmpeg](https://ffmpeg.org/):** used by yt-dlp for video/audio merging and conversion. Licensed under [LGPL/GPL](https://ffmpeg.org/legal.html). Builds from [yt-dlp/FFmpeg-Builds](https://github.com/yt-dlp/FFmpeg-Builds) for both Windows and Linux.
- **[Avalonia UI](https://avaloniaui.net/):** the cross-platform UI framework behind every build.
- **[YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode):** pure-C# YouTube search and extraction, used for search everywhere and for downloads in the phone apps. Licensed under [LGPL-3.0](https://github.com/Tyrrrz/YoutubeExplode/blob/master/License.txt).

## License

[MIT](LICENSE)
