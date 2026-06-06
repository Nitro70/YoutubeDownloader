using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace YouTubeDownloader;

public static class ToolsExtractor
{
    private static readonly string[] WindowsToolFiles = new[]
    {
        "yt-dlp.exe",
        "ffmpeg.exe",
        "ffprobe.exe",
        "avcodec-62.dll",
        "avdevice-62.dll",
        "avfilter-11.dll",
        "avformat-62.dll",
        "avutil-60.dll",
        "swresample-6.dll",
        "swscale-9.dll"
    };

    private static readonly string[] LinuxToolFiles = new[]
    {
        "yt-dlp",
        "ffmpeg",
        "ffprobe"
    };

    private static readonly string[] LinuxExecutables = new[] { "yt-dlp", "ffmpeg", "ffprobe" };

    public static string ToolsDirectory { get; private set; } = string.Empty;

    public static string YtDlpPath => Path.Combine(
        ToolsDirectory, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");

    public static string FfmpegDirectory => ToolsDirectory;

    private static string[] ToolFiles =>
        OperatingSystem.IsWindows() ? WindowsToolFiles : LinuxToolFiles;

    public static void ExtractTools()
    {
        ToolsDirectory = Path.Combine(GetDataDir(), "YouTubeDownloader", "tools");
        Directory.CreateDirectory(ToolsDirectory);

        var assembly = Assembly.GetExecutingAssembly();
        const string resourcePrefix = "YouTubeDownloader.Tools.";

        string appVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        string stampPath = Path.Combine(ToolsDirectory, "tools.version");
        string? existingStamp = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : null;
        bool versionChanged = existingStamp != appVersion;

        bool ytDlpName(string name) =>
            string.Equals(name, "yt-dlp.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "yt-dlp", StringComparison.OrdinalIgnoreCase);

        foreach (string toolFile in ToolFiles)
        {
            string destPath = Path.Combine(ToolsDirectory, toolFile);
            bool isYtDlp = ytDlpName(toolFile);

            // Re-extract if missing, or if version changed (except yt-dlp, whose self-update we preserve).
            bool shouldExtract = !File.Exists(destPath) || (versionChanged && !isYtDlp);
            if (shouldExtract)
            {
                ExtractResource(assembly, resourcePrefix + toolFile, destPath);
            }

            if (!OperatingSystem.IsWindows() && Array.IndexOf(LinuxExecutables, toolFile) >= 0)
            {
                TrySetExecutable(destPath);
            }
        }

        File.WriteAllText(stampPath, appVersion);
    }

    private static string GetDataDir()
    {
        // LocalApplicationData maps to:
        //   Windows: %LOCALAPPDATA%
        //   Linux:   $XDG_DATA_HOME or ~/.local/share
        //   macOS:   ~/.local/share (mono behavior); on net Core macOS it returns ~/.local/share too
        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private static void ExtractResource(Assembly assembly, string resourceName, string destPath)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new Exception($"Resource not found: {resourceName}");
        }

        string tempPath = destPath + ".tmp";
        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        {
            stream.CopyTo(fileStream);
        }

        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }
        File.Move(tempPath, destPath);
    }

    private static void TrySetExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            // .NET 7+ exposes File.SetUnixFileMode on Unix.
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Fall back to chmod if SetUnixFileMode is unavailable for some reason.
            try
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    ArgumentList = { "755", path },
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                p?.WaitForExit();
            }
            catch { /* best-effort */ }
        }
    }

    public static bool ToolsExist()
    {
        if (string.IsNullOrEmpty(ToolsDirectory)) return false;
        foreach (string toolFile in ToolFiles)
        {
            if (!File.Exists(Path.Combine(ToolsDirectory, toolFile))) return false;
        }
        return true;
    }
}
