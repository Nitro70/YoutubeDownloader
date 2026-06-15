using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace YouTubeDownloader;

/// <summary>
/// Command-line mode for the desktop app. Running the executable with no
/// arguments opens the GUI; running it with any argument routes here so the
/// same binary doubles as a CLI (yt-dlp front-end).
/// </summary>
internal static class CliRunner
{
    public static bool ShouldRunCli(string[] args) => args.Length > 0;

    public static int Run(string[] args)
    {
        NativeConsole.Ensure();

        var o = CliOptions.Parse(args, out string? parseError);

        if (o.ShowHelp)
        {
            PrintHelp();
            return 0;
        }
        if (o.ShowVersion)
        {
            Console.WriteLine($"YouTube Downloader {VersionString()}");
            return 0;
        }
        if (parseError != null)
        {
            Console.Error.WriteLine($"Error: {parseError}");
            Console.Error.WriteLine("Run with --help for usage.");
            return 2;
        }
        if (string.IsNullOrWhiteSpace(o.Url))
        {
            Console.Error.WriteLine("Error: no URL given.");
            Console.Error.WriteLine("Run with --help for usage.");
            return 2;
        }
        if (!IsValidHttpUrl(o.Url!))
        {
            Console.Error.WriteLine("Error: the URL must be a valid http(s) link.");
            return 2;
        }

        try
        {
            ToolsExtractor.ExtractTools();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to prepare tools (yt-dlp/ffmpeg): {ex.Message}");
            return 1;
        }

        string exeDir = AppContext.BaseDirectory;
        string cookies = Path.Combine(exeDir, "cookies.txt");
        string outputDir = string.IsNullOrWhiteSpace(o.OutputDir)
            ? Path.Combine(exeDir, "videos")
            : o.OutputDir!;

        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Cannot create output directory '{outputDir}': {ex.Message}");
            return 1;
        }

        return o.InfoOnly ? ShowInfo(o.Url!, cookies) : Download(o, outputDir, cookies);
    }

    private static int ShowInfo(string url, string cookies)
    {
        var args = new List<string>();
        if (File.Exists(cookies)) { args.Add("--cookies"); args.Add(cookies); }
        args.Add("--dump-json");
        args.Add("--no-playlist");
        args.Add(url);

        var psi = NewYtDlp(redirect: true);
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p == null) { Console.Error.WriteLine("Failed to start yt-dlp."); return 1; }

            string outp = p.StandardOutput.ReadToEnd();
            string errp = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(outp))
            {
                Console.Error.WriteLine(string.IsNullOrWhiteSpace(errp) ? "Could not fetch info." : errp.Trim());
                return 1;
            }

            var j = JObject.Parse(outp);
            string title = j["title"]?.ToString() ?? "Unknown";
            string channel = j["uploader"]?.ToString() ?? "Unknown";
            int duration = j["duration"]?.Value<int>() ?? 0;

            Console.WriteLine($"Title:    {title}");
            Console.WriteLine($"Channel:  {channel}");
            Console.WriteLine($"Duration: {duration / 60}:{duration % 60:D2}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int Download(CliOptions o, string outputDir, string cookies)
    {
        var args = BuildDownloadArgs(o, outputDir, cookies, out string? error);
        if (args == null)
        {
            Console.Error.WriteLine($"Error: {error}");
            return 2;
        }

        Console.WriteLine($"Downloading {(o.Audio ? "audio (mp3)" : $"video ({o.Quality})")}: {o.Url}");
        Console.WriteLine($"Saving to: {outputDir}");
        Console.WriteLine();

        // Don't redirect — let yt-dlp draw its own live progress in the console.
        var psi = NewYtDlp(redirect: false);
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p == null) { Console.Error.WriteLine("Failed to start yt-dlp."); return 1; }
            p.WaitForExit();

            Console.WriteLine();
            if (p.ExitCode == 0) Console.WriteLine("Done.");
            else Console.Error.WriteLine($"yt-dlp exited with code {p.ExitCode}.");
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static ProcessStartInfo NewYtDlp(bool redirect) => new()
    {
        FileName = ToolsExtractor.YtDlpPath,
        UseShellExecute = false,
        RedirectStandardOutput = redirect,
        RedirectStandardError = redirect,
        CreateNoWindow = redirect
    };

    private static List<string>? BuildDownloadArgs(CliOptions o, string outputDir, string cookies, out string? error)
    {
        error = null;
        var args = new List<string> { "--ffmpeg-location", ToolsExtractor.FfmpegDirectory };

        if (File.Exists(cookies)) { args.Add("--cookies"); args.Add(cookies); }
        args.Add("--newline");
        args.Add("--progress");

        string? name = string.IsNullOrWhiteSpace(o.Name) ? null : o.Name!.Trim();
        if (name != null && name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Filename contains characters that are not allowed.";
            return null;
        }

        string template = name != null && !o.Channel
            ? Path.Combine(outputDir, name + ".%(ext)s")
            : Path.Combine(outputDir, "%(title)s.%(ext)s");

        if (o.Audio)
        {
            args.Add("-x");
            args.Add("--audio-format"); args.Add("mp3");
            args.Add("--audio-quality"); args.Add("0");
        }
        else
        {
            string fmt = o.Quality == "best"
                ? "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best"
                : $"bestvideo[height<={o.Quality}][ext=mp4]+bestaudio[ext=m4a]/best[height<={o.Quality}][ext=mp4]/best";
            args.Add("-f"); args.Add(fmt);
            args.Add("--merge-output-format"); args.Add("mp4");
        }

        args.Add("-o"); args.Add(template);

        string url = o.Url!;
        if (o.Channel) { url = NormalizeChannelUrl(url); args.Add("--yes-playlist"); }
        else args.Add("--no-playlist");

        args.Add(url);
        return args;
    }

    private static bool IsValidHttpUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string NormalizeChannelUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        if (!uri.Host.ToLowerInvariant().EndsWith("youtube.com")) return url;

        string path = uri.AbsolutePath;
        bool isChannelPath = path.StartsWith("/@") || path.StartsWith("/c/") ||
                             path.StartsWith("/channel/") || path.StartsWith("/user/");
        if (!isChannelPath) return url;

        string trimmed = path.TrimEnd('/');
        if (!trimmed.EndsWith("/videos")) trimmed += "/videos";
        return new UriBuilder(uri) { Path = trimmed }.Uri.ToString();
    }

    private static string VersionString() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    private static void PrintHelp()
    {
        string exe = OperatingSystem.IsWindows() ? "YouTubeDownloader.exe" : "YouTubeDownloader";
        Console.WriteLine($"""
            YouTube Downloader {VersionString()} — yt-dlp + ffmpeg, GUI or command line.

            Run with no arguments to open the graphical interface.
            Run with any option below to use the command line instead.

            USAGE
              {exe} [options] <URL>

            ACTIONS
              -d, --download         Download the video (default when a URL is given)
              -i, --info             Print title, channel and duration, then exit
              -h, --help, /?         Show this help and exit
              -v, --version          Show the version and exit

            FORMAT
              -a, --audio, --mp3     Download audio only, converted to MP3
              -q, --quality <Q>      Video quality: best, 1080, 720, 480, 360
                                     (default: best; ignored with --audio)

            OUTPUT
              -n, --name <NAME>      Output filename without extension
                                     (ignored for --channel)
              -O, --dir <DIR>        Save folder (default: a 'videos' folder next
                                     to the executable)

            SOURCE
              -u, --url <URL>        The video/channel URL (or pass it positionally)
              -c, --channel          Download the entire channel / playlist

            NOTES
              * Put a cookies.txt next to the executable for age-restricted videos
                (used automatically, same as the GUI).
              * Windows-style /flags also work, e.g. /help, /d, /q 720.

            EXAMPLES
              {exe} https://youtu.be/VIDEO
              {exe} -d -q 1080 -n "my clip" https://youtu.be/VIDEO
              {exe} --mp3 https://youtu.be/VIDEO
              {exe} -i https://youtu.be/VIDEO
              {exe} -c https://www.youtube.com/@SomeChannel
            """);
    }

    /// <summary>Parsed command-line options.</summary>
    private sealed class CliOptions
    {
        public string? Url;
        public bool Audio;
        public string Quality = "best";
        public string? Name;
        public string? OutputDir;
        public bool Channel;
        public bool InfoOnly;
        public bool ShowHelp;
        public bool ShowVersion;

        public static CliOptions Parse(string[] args, out string? error)
        {
            error = null;
            var o = new CliOptions();

            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                string key = Canonicalize(raw);

                switch (key)
                {
                    case "-h": case "--help":
                        o.ShowHelp = true; break;
                    case "-v": case "--version":
                        o.ShowVersion = true; break;
                    case "-d": case "--download":
                        /* download is the default action */ break;
                    case "-i": case "--info":
                        o.InfoOnly = true; break;
                    case "-a": case "--audio": case "--mp3":
                        o.Audio = true; break;
                    case "-c": case "--channel":
                        o.Channel = true; break;

                    case "-q": case "--quality":
                        if (!Next(args, ref i, out string qv)) { error = "--quality needs a value"; return o; }
                        o.Quality = NormalizeQuality(qv, ref error);
                        if (error != null) return o;
                        break;
                    case "-n": case "--name":
                        if (!Next(args, ref i, out string nv)) { error = "--name needs a value"; return o; }
                        o.Name = nv; break;
                    case "-O": case "--dir": case "--output-dir":
                        if (!Next(args, ref i, out string dv)) { error = "--dir needs a value"; return o; }
                        o.OutputDir = dv; break;
                    case "-u": case "--url":
                        if (!Next(args, ref i, out string uv)) { error = "--url needs a value"; return o; }
                        if (o.Url != null) { error = "more than one URL was given"; return o; }
                        o.Url = uv; break;

                    default:
                        if (raw.StartsWith("-") || raw.StartsWith("/"))
                        {
                            error = $"Unknown option: {raw}";
                            return o;
                        }
                        if (o.Url != null) { error = $"unexpected argument: {raw}"; return o; }
                        o.Url = raw;
                        break;
                }
            }

            return o;
        }

        private static bool Next(string[] args, ref int i, out string value)
        {
            if (i + 1 >= args.Length) { value = string.Empty; return false; }
            value = args[++i];
            return true;
        }

        // Map Windows-style /flags to dash form; lowercase long options so
        // matching is case-insensitive while short flags (e.g. -O) keep case.
        private static string Canonicalize(string a)
        {
            if (a == "/?") return "--help";
            if (a.StartsWith("/"))
            {
                string body = a.Substring(1);
                a = body.Length == 1 ? "-" + body : "--" + body;
            }
            return a.StartsWith("--") ? a.ToLowerInvariant() : a;
        }

        private static string NormalizeQuality(string q, ref string? error)
        {
            string s = q.Trim().ToLowerInvariant().TrimEnd('p');
            switch (s)
            {
                case "best": return "best";
                case "1080":
                case "720":
                case "480":
                case "360": return s;
                default:
                    error = $"invalid quality '{q}' (use best, 1080, 720, 480 or 360)";
                    return "best";
            }
        }
    }

    /// <summary>
    /// The desktop app is a GUI-subsystem binary, so on Windows it has no
    /// console of its own. In CLI mode we attach to the launching terminal (or
    /// keep the redirected handle) so Console output is visible.
    /// </summary>
    private static class NativeConsole
    {
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        public static void Ensure()
        {
            if (!OperatingSystem.IsWindows()) return; // Linux already has stdio

            // Attach to the parent console. If output is redirected to a file or
            // pipe this fails harmlessly and the redirected handle is used.
            AttachConsole(ATTACH_PARENT_PROCESS);

            try
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(stdout);
                var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                Console.SetError(stderr);
            }
            catch
            {
                // No usable standard handles; nothing more we can do.
            }
        }
    }
}
