using Foundation;
using Photos;

namespace YouTubeDownloader.iOS;

/// <summary>Puts downloaded videos into the Photos app.</summary>
internal static class PhotoLibrary
{
    private const string SaveVideosKey = "SaveVideosToPhotos";

    /// <summary>Running inside LiveContainer, which sets this for the apps it runs.</summary>
    public static bool InLiveContainer => Environment.GetEnvironmentVariable("LC_HOME_PATH") is not null;

    /// <summary>
    /// False in LiveContainer's multitasking mode. Apps run inside its LiveProcess extension
    /// there, whose Info.plist has no photo library usage text, and iOS ends an app that asks
    /// for Photos access without one. LiveContainer's normal mode has the text, and so does
    /// this app's own Info.plist.
    /// </summary>
    public static bool IsAvailable => Environment.GetEnvironmentVariable("LP_HOME_PATH") is null;

    /// <summary>The switch in the app, remembered between launches. On unless turned off.</summary>
    public static bool SaveVideos
    {
        get => NSUserDefaults.StandardUserDefaults[SaveVideosKey] is NSNumber on ? on.BoolValue : true;
        set => NSUserDefaults.StandardUserDefaults.SetBool(value, SaveVideosKey);
    }

    /// <summary>Adds the video to Photos. Returns null when it worked, else why it didn't.</summary>
    public static async Task<string?> TrySaveVideoAsync(string path)
    {
        // Add-only access is all this needs; that request exists from iOS 14, before that
        // PHPhotoLibrary asks for full access. Neither one answers Limited.
        var access = OperatingSystem.IsIOSVersionAtLeast(14)
            ? await PHPhotoLibrary.RequestAuthorizationAsync(PHAccessLevel.AddOnly)
            : await PHPhotoLibrary.RequestAuthorizationAsync();
        if (access != PHAuthorizationStatus.Authorized)
        {
            // Inside LiveContainer the permission belongs to LiveContainer, not to this app.
            string app = InLiveContainer ? "LiveContainer" : "YT Downloader";
            string privacy = OperatingSystem.IsIOSVersionAtLeast(16) ? "Privacy & Security" : "Privacy";
            return $"Photos access is off for {app} (Settings > {privacy} > Photos)";
        }

        var saved = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () => PHAssetChangeRequest.FromVideo(NSUrl.FromFilename(path)),
            (ok, error) => saved.TrySetResult(ok ? null : error?.LocalizedDescription ?? "Photos didn't accept the video"));
        return await saved.Task;
    }
}
