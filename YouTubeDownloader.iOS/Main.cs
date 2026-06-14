using UIKit;

namespace YouTubeDownloader.iOS;

// NOTE: do not name this class "Application" — that collides with
// Avalonia.Application and breaks `App : Application` in App.axaml.cs.
public static class Program
{
    // This is the main entry point of the application.
    private static void Main(string[] args)
    {
        // If you want to use a different Application Delegate class from "AppDelegate"
        // you can specify it here.
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
