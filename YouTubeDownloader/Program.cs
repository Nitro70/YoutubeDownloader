using System;
using Avalonia;

namespace YouTubeDownloader;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // No arguments → open the GUI. Any arguments → command-line mode.
        if (CliRunner.ShouldRunCli(args))
            return CliRunner.Run(args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
