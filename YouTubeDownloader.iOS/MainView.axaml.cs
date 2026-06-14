using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using YouTubeDownloader.Core;

namespace YouTubeDownloader.iOS;

public partial class MainView : UserControl
{
    private readonly YouTubeService _service = new();
    private readonly string _outputDirectory;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public MainView()
    {
        InitializeComponent();

        // App sandbox Documents folder. Exposed in the Files app via the
        // UIFileSharingEnabled / LSSupportsOpeningDocumentsInPlace plist keys.
        _outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Downloads");
        Directory.CreateDirectory(_outputDirectory);
    }

    private TopLevel? Top => TopLevel.GetTopLevel(this);

    private void UrlTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UrlPlaceholder.IsVisible = string.IsNullOrEmpty(UrlTextBox.Text);
    }

    private async void PasteButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clip = Top?.Clipboard;
            if (clip == null) return;
            string? text = await clip.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text)) UrlTextBox.Text = text.Trim();
        }
        catch
        {
            // ignore
        }
    }

    private async void FetchButton_Click(object? sender, RoutedEventArgs e)
    {
        string url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (!IsValidUrl(url))
        {
            SetStatus("Enter a valid YouTube link.");
            return;
        }

        FetchButton.IsEnabled = false;
        SetStatus("Fetching info…");
        try
        {
            var info = await _service.GetVideoInfoAsync(url);
            VideoTitleText.Text = info.Title;
            ChannelText.Text = $"Channel: {info.Author}";
            DurationText.Text = $"Duration: {info.DurationDisplay}";
            VideoInfoPanel.IsVisible = true;
            SetStatus("Ready");

            if (!string.IsNullOrEmpty(info.ThumbnailUrl))
                await LoadThumbnailAsync(info.ThumbnailUrl);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't fetch: {ex.Message}");
        }
        finally
        {
            FetchButton.IsEnabled = true;
        }
    }

    private async Task LoadThumbnailAsync(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var bytes = await client.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            await Dispatcher.UIThread.InvokeAsync(() => ThumbnailImage.Source = bmp);
        }
        catch
        {
            // ignore thumbnail errors
        }
    }

    private async void DownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (!IsValidUrl(url))
        {
            SetStatus("Enter a valid YouTube link.");
            return;
        }

        var kind = AudioRadio.IsChecked == true ? DownloadKind.Audio : DownloadKind.Video;
        string? customName = FilenameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(customName)) customName = null;

        _busy = true;
        _cts = new CancellationTokenSource();
        DownloadButton.IsVisible = false;
        CancelButton.IsVisible = true;
        DownloadProgressBar.Value = 0;
        SetStatus("Starting…");

        var progress = new Progress<double>(p => Dispatcher.UIThread.Post(() =>
        {
            DownloadProgressBar.Value = p * 100;
            StatusText.Text = $"Downloading… {p:P0}";
        }));

        try
        {
            string path = await _service.DownloadAsync(url, kind, _outputDirectory, customName, progress, _cts.Token);
            DownloadProgressBar.Value = 100;
            SetStatus($"Saved: {Path.GetFileName(path)}");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            DownloadButton.IsVisible = true;
            CancelButton.IsVisible = false;
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void SetStatus(string text)
    {
        Dispatcher.UIThread.Post(() => StatusText.Text = text);
    }

    private static bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase));
    }
}
