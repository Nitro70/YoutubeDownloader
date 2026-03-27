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
            // Use AppData/Local for extracted tools
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ToolsDirectory = Path.Combine(appData, "YouTubeDownloader", "tools");

            Directory.CreateDirectory(ToolsDirectory);

            var assembly = Assembly.GetExecutingAssembly();
            string resourcePrefix = "YouTubeDownloader.Tools.";

            foreach (string toolFile in ToolFiles)
            {
                string destPath = Path.Combine(ToolsDirectory, toolFile);

                // Only extract if file doesn't exist or is outdated
                if (!File.Exists(destPath))
                {
                    ExtractResource(assembly, resourcePrefix + toolFile, destPath);
                }
            }
        }

        private static void ExtractResource(Assembly assembly, string resourceName, string destPath)
        {
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new Exception($"Resource not found: {resourceName}");
            }

            using FileStream fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fileStream);
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
