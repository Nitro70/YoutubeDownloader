using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace YouTubeDownloader.Droid;

// Base type is fully qualified: the Android SDK's implicit usings pull in
// Android.App.Application, which would otherwise be ambiguous with Avalonia's.
public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Avalonia 12 asks each new activity for its view. Android recreates the activity for
        // changes the manifest doesn't handle, so hand back the same view every time: a new
        // one would show an idle form while the old one's download carried on unseen.
        if (ApplicationLifetime is IActivityApplicationLifetime activities)
        {
            MainView? view = null;
            activities.MainViewFactory = () => view ??= new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
