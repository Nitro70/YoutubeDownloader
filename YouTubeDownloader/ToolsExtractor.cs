using System;
using System.IO;
using System.Reflection;

namespace YouTubeDownloader
{
    public static class ToolsExtractor
    {
        private static readonly string[] ToolFiles = new[]
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

        public static string ToolsDirectory { get; private set; } = string.Empty;
        public static string YtDlpPath => Path.Combine(ToolsDirectory, "yt-dlp.exe");
        public static string FfmpegDirectory => ToolsDirectory;

        public static void ExtractTools()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ToolsDirectory = Path.Combine(appData, "YouTubeDownloader", "tools");

            Directory.CreateDirectory(ToolsDirectory);

            var assembly = Assembly.GetExecutingAssembly();
            string resourcePrefix = "YouTubeDownloader.Tools.";

            // Version stamp lets us re-extract when the app ships new embedded tools.
            // yt-dlp.exe itself is excluded so its self-update is preserved.
            string appVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            string stampPath = Path.Combine(ToolsDirectory, "tools.version");
            string? existingStamp = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : null;
            bool versionChanged = existingStamp != appVersion;

            foreach (string toolFile in ToolFiles)
            {
                string destPath = Path.Combine(ToolsDirectory, toolFile);
                bool isYtDlp = string.Equals(toolFile, "yt-dlp.exe", StringComparison.OrdinalIgnoreCase);

                // Re-extract if missing, or if version changed (but never overwrite a self-updated yt-dlp).
                bool shouldExtract = !File.Exists(destPath) || (versionChanged && !isYtDlp);

                if (shouldExtract)
                {
                    ExtractResource(assembly, resourcePrefix + toolFile, destPath);
                }
            }

            File.WriteAllText(stampPath, appVersion);
        }

        private static void ExtractResource(Assembly assembly, string resourceName, string destPath)
        {
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new Exception($"Resource not found: {resourceName}");
            }

            // Write to a temp file then move into place so we don't corrupt a partially-written
            // tool if extraction fails mid-stream (or the file is briefly locked).
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

        public static bool ToolsExist()
        {
            if (string.IsNullOrEmpty(ToolsDirectory))
                return false;

            foreach (string toolFile in ToolFiles)
            {
                if (!File.Exists(Path.Combine(ToolsDirectory, toolFile)))
                    return false;
            }
            return true;
        }
    }
}
