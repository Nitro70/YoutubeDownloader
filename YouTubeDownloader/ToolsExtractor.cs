using System;
using System.IO;
using System.Reflection;

namespace YouTubeDownloader;

public static class ToolsExtractor
{
    private const string ResourcePrefix = "YouTubeDownloader.Tools.";

    public static string ToolsDirectory { get; private set; } = string.Empty;

    public static string YtDlpPath => Path.Combine(
        ToolsDirectory, OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");

    public static string FfmpegDirectory => ToolsDirectory;

    public static void ExtractTools()
    {
        ToolsDirectory = Path.Combine(GetDataDir(), "YouTubeDownloader", "tools");
        Directory.CreateDirectory(ToolsDirectory);

        var assembly = Assembly.GetExecutingAssembly();

        // Extract whatever tool resources are embedded — names are not
        // hardcoded, so a new ffmpeg build with different DLL version numbers
        // still extracts correctly.
        string appVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        string stampPath = Path.Combine(ToolsDirectory, "tools.version");
        string? existingStamp = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : null;
        bool versionChanged = existingStamp != appVersion;

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;

            string fileName = resource.Substring(ResourcePrefix.Length);
            string destPath = Path.Combine(ToolsDirectory, fileName);
            bool isYtDlp = IsYtDlp(fileName);

            // Re-extract if missing, or if the app version changed (except
            // yt-dlp, whose in-place self-update we want to preserve).
            bool shouldExtract = !File.Exists(destPath) || (versionChanged && !isYtDlp);
            if (shouldExtract)
                ExtractResource(assembly, resource, destPath);

            if (!OperatingSystem.IsWindows() && IsUnixExecutable(fileName))
                TrySetExecutable(destPath);
        }

        File.WriteAllText(stampPath, appVersion);
    }

    private static bool IsYtDlp(string fileName) =>
        string.Equals(fileName, "yt-dlp.exe", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "yt-dlp", StringComparison.OrdinalIgnoreCase);

    // On Unix the tool binaries have no file extension; mark them executable.
    private static bool IsUnixExecutable(string fileName) =>
        !Path.HasExtension(fileName);

    private static string GetDataDir() =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static void ExtractResource(Assembly assembly, string resourceName, string destPath)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new Exception($"Resource not found: {resourceName}");

        // Write to a temp file then move into place so a failure mid-copy (or a
        // briefly-locked file) can't leave a corrupt tool behind.
        string tempPath = destPath + ".tmp";
        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
        {
            stream.CopyTo(fileStream);
        }

        if (File.Exists(destPath))
            File.Delete(destPath);
        File.Move(tempPath, destPath);
    }

    private static void TrySetExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch
        {
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

    public static bool ToolsExist() =>
        !string.IsNullOrEmpty(ToolsDirectory) && File.Exists(YtDlpPath);
}
