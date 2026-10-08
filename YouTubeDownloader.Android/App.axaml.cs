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
        // Android can create more than one activity over the app's life, so Avalonia 12
        // asks for a way to make the view rather than a single view.
        if (ApplicationLifetime is IActivityApplicationLifetime activities)
        {
            activities.MainViewFactory = () => new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
