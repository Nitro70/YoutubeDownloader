using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace YouTubeDownloader.iOS;

// The UIApplicationDelegate for the application. This class is responsible for
// launching the Avalonia application as well as handling iOS app lifecycle events.
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
