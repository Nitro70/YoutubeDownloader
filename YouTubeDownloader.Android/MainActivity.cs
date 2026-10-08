using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace YouTubeDownloader.Droid;

[Activity(
    Label = "YT Downloader",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    // Handle these in place instead of recreating the activity (split screen, foldables,
    // font size, language, keyboards).
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density
        | ConfigChanges.FontScale | ConfigChanges.Locale | ConfigChanges.LayoutDirection
        | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public class MainActivity : AvaloniaMainActivity
{
}

// Avalonia 12 starts the Avalonia app from the Android Application rather than the activity.
[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
