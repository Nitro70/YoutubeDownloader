using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Newtonsoft.Json.Linq;
using MsgIcon = MsBox.Avalonia.Enums.Icon;

namespace YouTubeDownloader;

public partial class MainWindow : Window
{
    private static readonly char[] InvalidFilenameChars = Path.GetInvalidFileNameChars();
    private const int MaxLogRows = 1000;

    private readonly string _outputDirectory;
    private readonly string _cookiesPath;

    private Process? _currentProcess;
    private readonly object _processLock = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isDownloading;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            ToolsExtractor.ExtractTools();
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync("Error", $"Failed to extract tools: {ex.Message}", MsgIcon.Error);
        }

        string exeDir = AppContext.BaseDirectory;
        _outputDirectory = Path.Combine(exeDir, "videos");
        _cookiesPath = Path.Combine(exeDir, "cookies.txt");
        Directory.CreateDirectory(_outputDirectory);

        OutputDirTextBox.Text = _outputDirectory;
        Log("YouTube Downloader started");
        Log($"Tools location: {ToolsExtractor.ToolsDirectory}");

        if (File.Exists(_cookiesPath))
        {
            Log("Cookies file found - age-restricted videos supported.");
        }

        Opened += async (_, _) => await TryAutoPasteUrlAsync();
        _ = UpdateYtDlpAsync();
    }

    private async Task TryAutoPasteUrlAsync()
    {
        try
        {
            var clipboard = Clipboard;
            if (clipboard == null) return;
            string? text = await clipboard.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length < 2048 &&
                Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                (uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase)))
            {
                UrlTextBox.Text = text;
                Log("Auto-detected YouTube URL from clipboard.");
            }
        }
        catch
        {
            // Clipboard access may fail; ignore.
        }
    }

    private async Task UpdateYtDlpAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ToolsExtractor.YtDlpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-U");

            using var process = Process.Start(startInfo);
            if (process == null) return;

            string output = await process.StandardOutput.ReadToEndAsync();
            _ = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrEmpty(output))
            {
                Log($"yt-dlp update: {output.Trim()}");
            }
        }
        catch
        {
            // Best-effort
        }
    }

    private void Log(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogListBox.Items.Add(message);
            while (LogListBox.Items.Count > MaxLogRows)
            {
                LogListBox.Items.RemoveAt(0);
            }
            if (LogListBox.Items.Count > 0)
            {
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            }
        }, DispatcherPriority.Background);
    }

    private void ClearLog()
    {
        Dispatcher.UIThread.Post(() => LogListBox.Items.Clear());
    }

    private Task ShowMessageAsync(string title, string message, MsgIcon icon = MsgIcon.Info, ButtonEnum buttons = ButtonEnum.Ok)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, buttons, icon);
        return box.ShowWindowDialogAsync(this);
    }

    private async void PasteButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = Clipboard;
            if (clipboard == null) return;
            string? text = await clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(text)) UrlTextBox.Text = text;
        }
        catch
        {
            // ignore
        }
    }

    private async void FetchInfoButton_Click(object? sender, RoutedEventArgs e)
    {
        string url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (!IsValidHttpUrl(url))
        {
            await ShowMessageAsync("Warning", "Please enter a valid http(s) URL", MsgIcon.Warning);
            return;
        }

        FetchInfoButton.IsEnabled = false;
        Log("Fetching video information...");

        try
        {
            var info = await FetchVideoInfoAsync(url);
            if (info != null) await DisplayVideoInfoAsync(info);
        }
        catch (Exception ex)
        {
            Log($"Error fetching info: {ex.Message}");
        }
        finally
        {
            FetchInfoButton.IsEnabled = true;
        }
    }

    private async Task<JObject?> FetchVideoInfoAsync(string url)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ToolsExtractor.YtDlpPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (File.Exists(_cookiesPath))
            {
                startInfo.ArgumentList.Add("--cookies");
                startInfo.ArgumentList.Add(_cookiesPath);
            }
            startInfo.ArgumentList.Add("--dump-json");
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add(url);

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                return JObject.Parse(output);
            }

            Log($"Error: {error}");
            return null;
        }
        catch (Exception ex)
        {
            Log($"Exception: {ex.Message}");
            return null;
        }
    }

    private async Task DisplayVideoInfoAsync(JObject info)
    {
        VideoInfoPanel.IsVisible = true;

        string title = info["title"]?.ToString() ?? "Unknown";
        VideoTitleText.Text = TruncateForDisplay(title, 80);

        string channel = info["uploader"]?.ToString() ?? "Unknown";
        ChannelText.Text = $"Channel: {channel}";

        int duration = info["duration"]?.Value<int>() ?? 0;
        int minutes = duration / 60;
        int seconds = duration % 60;
        DurationText.Text = $"Duration: {minutes}:{seconds:D2}";

        Log($"Found: {title}");

        string? thumbnailUrl = info["thumbnail"]?.ToString();
        if (!string.IsNullOrEmpty(thumbnailUrl))
        {
            await LoadThumbnailAsync(thumbnailUrl);
        }
    }

    private static string TruncateForDisplay(string s, int max)
    {
        if (s.Length <= max) return s;
        int cut = max - 3;
        if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
        return s.Substring(0, cut) + "...";
    }

    private async Task LoadThumbnailAsync(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var imageData = await client.GetByteArrayAsync(url);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var ms = new MemoryStream(imageData);
                ThumbnailImage.Source = new Bitmap(ms);
            });
        }
        catch
        {
            // ignore
        }
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var sp = StorageProvider;
            if (sp == null) return;

            IStorageFolder? startFolder = null;
            try
            {
                if (Directory.Exists(OutputDirTextBox.Text))
                {
                    startFolder = await sp.TryGetFolderFromPathAsync(OutputDirTextBox.Text!);
                }
            }
            catch { /* fall through to default */ }

            var picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select download folder",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder
            });
            if (picked.Count > 0)
            {
                string? path = picked[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) OutputDirTextBox.Text = path;
            }
        }
        catch (Exception ex)
        {
            Log($"Browse error: {ex.Message}");
        }
    }

    private void OpenFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        string? path = OutputDirTextBox.Text;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // Fall back to a platform-specific opener if shell-execute fails on Linux.
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsLinux() ? "xdg-open" :
                               OperatingSystem.IsMacOS() ? "open" : "explorer",
                    ArgumentList = { path },
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Log($"Could not open folder: {ex.Message}");
            }
        }
    }

    private void FormatRadio_Changed(object? sender, RoutedEventArgs e)
    {
        if (QualityComboBox != null)
        {
            QualityComboBox.IsEnabled = Mp4Radio.IsChecked == true;
        }
    }

    private void UrlTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && FetchInfoButton.IsEnabled && !_isDownloading)
        {
            e.Handled = true;
            FetchInfoButton_Click(FetchInfoButton, new RoutedEventArgs());
        }
    }

    private void UrlTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UrlPlaceholder.IsVisible = string.IsNullOrEmpty(UrlTextBox.Text);
    }

    private async void DownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        string url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (!IsValidHttpUrl(url))
        {
            await ShowMessageAsync("Warning", "Please enter a valid http(s) URL", MsgIcon.Warning);
            return;
        }
        if (_isDownloading) return;
        await StartDownloadAsync(url, isChannel: false);
    }

    private async void ChannelDownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        string url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (!IsValidHttpUrl(url))
        {
            await ShowMessageAsync("Warning", "Please enter a valid http(s) channel URL", MsgIcon.Warning);
            return;
        }
        if (_isDownloading) return;

        var confirm = MessageBoxManager.GetMessageBoxStandard(
            "Download Entire Channel",
            "This will download ALL videos from the channel.\n\nThis may take a very long time and use significant storage space.\n\nContinue?",
            ButtonEnum.YesNo, MsgIcon.Question);

        var result = await confirm.ShowWindowDialogAsync(this);
        if (result == ButtonResult.Yes)
        {
            await StartDownloadAsync(url, isChannel: true);
        }
    }

    private static bool IsValidHttpUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool TryValidateFilename(string filename, out string error)
    {
        error = string.Empty;
        if (filename.IndexOfAny(InvalidFilenameChars) >= 0)
        {
            error = "Filename contains characters that are not allowed.";
            return false;
        }
        return true;
    }

    private async Task StartDownloadAsync(string url, bool isChannel)
    {
        _isDownloading = true;
        _cancellationTokenSource = new CancellationTokenSource();

        DownloadButton.IsVisible = false;
        ChannelDownloadButton.IsVisible = false;
        CancelButton.IsVisible = true;
        ShowInFolderButton.IsVisible = false;
        DownloadProgressBar.Value = 0;
        ProgressStatsText.Text = string.Empty;
        ProgressText.Text = "Starting...";
        ClearLog();

        try
        {
            var args = BuildDownloadArguments(url, isChannel, out string? validationError);
            if (args == null)
            {
                Log($"Error: {validationError}");
                await ShowMessageAsync("Invalid Input", validationError ?? "Invalid input.", MsgIcon.Warning);
                return;
            }

            Log("Starting download...");
            Log($"Command: yt-dlp {string.Join(" ", args)}");

            await RunDownloadProcessAsync(args, _cancellationTokenSource.Token);

            if (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                DownloadProgressBar.Value = 100;
                ProgressText.Text = "Download complete!";
                ProgressStatsText.Text = string.Empty;
                ShowInFolderButton.IsVisible = true;
                Log("\n Download completed successfully!");
            }
        }
        catch (OperationCanceledException)
        {
            Log("\n Download cancelled by user");
            ProgressText.Text = "Download cancelled";
        }
        catch (Exception ex)
        {
            Log($"\n Error: {ex.Message}");
            ProgressText.Text = "Download failed";
        }
        finally
        {
            _isDownloading = false;
            lock (_processLock)
            {
                _currentProcess?.Dispose();
                _currentProcess = null;
            }
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            DownloadButton.IsVisible = true;
            ChannelDownloadButton.IsVisible = true;
            CancelButton.IsVisible = false;
        }
    }

    private static string NormalizeChannelUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        string host = uri.Host.ToLowerInvariant();
        if (!host.EndsWith("youtube.com")) return url;

        string path = uri.AbsolutePath;
        bool isChannelPath = path.StartsWith("/@") || path.StartsWith("/c/") ||
                             path.StartsWith("/channel/") || path.StartsWith("/user/");
        if (!isChannelPath) return url;

        string trimmed = path.TrimEnd('/');
        if (!trimmed.EndsWith("/videos")) trimmed += "/videos";

        var builder = new UriBuilder(uri) { Path = trimmed };
        return builder.Uri.ToString();
    }

    private List<string>? BuildDownloadArguments(string url, bool isChannel, out string? error)
    {
        error = null;
        var args = new List<string>
        {
            "--ffmpeg-location", ToolsExtractor.FfmpegDirectory
        };

        if (File.Exists(_cookiesPath))
        {
            args.Add("--cookies");
            args.Add(_cookiesPath);
        }

        args.Add("--newline");
        args.Add("--progress");

        bool isMp3 = Mp3Radio.IsChecked == true;
        string outputDir = OutputDirTextBox.Text ?? _outputDirectory;
        string filename = (FilenameTextBox.Text ?? string.Empty).Trim();

        if (!string.IsNullOrEmpty(filename) && !TryValidateFilename(filename, out string fnError))
        {
            error = fnError;
            return null;
        }

        string outputTemplate = !string.IsNullOrEmpty(filename) && !isChannel
            ? Path.Combine(outputDir, filename + ".%(ext)s")
            : Path.Combine(outputDir, "%(title)s.%(ext)s");

        if (isMp3)
        {
            args.Add("-x");
            args.Add("--audio-format"); args.Add("mp3");
            args.Add("--audio-quality"); args.Add("0");
        }
        else
        {
            string quality = (QualityComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Best";
            string formatSpec = quality == "Best"
                ? "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best"
                : $"bestvideo[height<={quality.Replace("p", "")}][ext=mp4]+bestaudio[ext=m4a]/best[height<={quality.Replace("p", "")}][ext=mp4]/best";
            args.Add("-f"); args.Add(formatSpec);
            args.Add("--merge-output-format"); args.Add("mp4");
        }

        args.Add("-o"); args.Add(outputTemplate);

        if (isChannel)
        {
            url = NormalizeChannelUrl(url);
            args.Add("--yes-playlist");
        }
        else
        {
            args.Add("--no-playlist");
        }

        args.Add(url);
        return args;
    }

    private async Task RunDownloadProcessAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ToolsExtractor.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in arguments) startInfo.ArgumentList.Add(a);

        Process process;
        lock (_processLock)
        {
            _currentProcess = new Process { StartInfo = startInfo };
            process = _currentProcess;
        }

        process.Start();

        using var killReg = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
        });

        var outputTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrEmpty(line)) ProcessOutputLine(line);
            }
        });

        var errorTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrEmpty(line)) Log(line);
            }
        });

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        finally
        {
            try { await Task.WhenAll(outputTask, errorTask); } catch { }
        }

        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        if (process.ExitCode != 0)
            throw new Exception($"yt-dlp exited with code {process.ExitCode}");
    }

    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+(?<pct>\d+\.?\d*)%(?:\s+of\s+~?\s*(?<size>[\d.]+\s*\w+))?(?:\s+at\s+(?<speed>[\d.]+\s*\w+/s|Unknown\s+B/s))?(?:\s+ETA\s+(?<eta>[\d:-]+))?",
        RegexOptions.Compiled);

    private void ProcessOutputLine(string line)
    {
        var match = ProgressRegex.Match(line);
        if (match.Success && double.TryParse(match.Groups["pct"].Value, out double progress))
        {
            string size = match.Groups["size"].Value.Trim();
            string speed = match.Groups["speed"].Value.Trim();
            string eta = match.Groups["eta"].Value.Trim();

            Dispatcher.UIThread.Post(() =>
            {
                DownloadProgressBar.Value = progress;
                ProgressText.Text = $"Downloading: {progress:F1}%";

                var stats = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(size)) stats.Append(size);
                if (!string.IsNullOrEmpty(speed))
                {
                    if (stats.Length > 0) stats.Append("  •  ");
                    stats.Append(speed);
                }
                if (!string.IsNullOrEmpty(eta) && eta != "-:-")
                {
                    if (stats.Length > 0) stats.Append("  •  ");
                    stats.Append("ETA ").Append(eta);
                }
                ProgressStatsText.Text = stats.ToString();
            });
            return;
        }

        if (line.Contains("has already been downloaded"))
        {
            Dispatcher.UIThread.Post(() =>
            {
                DownloadProgressBar.Value = 100;
                ProgressText.Text = "Already downloaded";
            });
        }

        Log(line);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isDownloading)
        {
            e.Cancel = true; // pause close until user answers
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Download in Progress",
                "A download is in progress. Cancel and exit?",
                ButtonEnum.YesNo, MsgIcon.Question);
            var result = await box.ShowWindowDialogAsync(this);
            if (result == ButtonResult.Yes)
            {
                _cancellationTokenSource?.Cancel();
                // Now close for real.
                _isDownloading = false;
                Close();
            }
            return;
        }
        base.OnClosing(e);
    }
}
