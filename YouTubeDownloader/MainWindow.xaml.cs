using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace YouTubeDownloader
{
    public partial class MainWindow : Window
    {
        private static readonly char[] InvalidFilenameChars =
            Path.GetInvalidFileNameChars();

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
                MessageBox.Show($"Failed to extract tools: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
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

            TryAutoPasteUrl();

            // Fire-and-forget; UpdateYtDlpAsync handles its own exceptions.
            _ = UpdateYtDlpAsync();
        }

        private void TryAutoPasteUrl()
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                string text = Clipboard.GetText().Trim();
                if (text.Length > 0 && text.Length < 2048 &&
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
                // Clipboard access can throw if another app has it locked; ignore.
            }
        }

        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && FetchInfoButton.IsEnabled && !_isDownloading)
            {
                e.Handled = true;
                FetchInfoButton_Click(FetchInfoButton, new RoutedEventArgs());
            }
        }

        private void UrlTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UrlPlaceholder.Visibility = string.IsNullOrEmpty(UrlTextBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
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
                // Best-effort update; ignore failures.
            }
        }

        private void Log(string message)
        {
            // BeginInvoke avoids blocking the caller; safe to call from worker threads.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
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
            }));
        }

        private void ClearLog()
        {
            Dispatcher.BeginInvoke(new Action(() => LogListBox.Items.Clear()));
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                UrlTextBox.Text = Clipboard.GetText();
            }
        }

        private async void FetchInfoButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (!IsValidHttpUrl(url))
            {
                MessageBox.Show("Please enter a valid http(s) URL", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FetchInfoButton.IsEnabled = false;
            Log("Fetching video information...");

            try
            {
                var info = await FetchVideoInfoAsync(url);
                if (info != null)
                {
                    DisplayVideoInfo(info);
                }
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

        private async void DisplayVideoInfo(JObject info)
        {
            VideoInfoPanel.Visibility = Visibility.Visible;

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
            // Don't split a surrogate pair.
            if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut) + "...";
        }

        private async Task LoadThumbnailAsync(string url)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var imageData = await client.GetByteArrayAsync(url);

                await Dispatcher.InvokeAsync(() =>
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(imageData);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    ThumbnailImage.Source = bitmap;
                });
            }
            catch
            {
                // Ignore thumbnail errors
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select download folder",
                SelectedPath = OutputDirTextBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputDirTextBox.Text = dialog.SelectedPath;
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string path = OutputDirTextBox.Text;
            if (Directory.Exists(path))
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }

        private void FormatRadio_Changed(object sender, RoutedEventArgs e)
        {
            if (QualityComboBox != null)
            {
                QualityComboBox.IsEnabled = Mp4Radio.IsChecked == true;
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (!IsValidHttpUrl(url))
            {
                MessageBox.Show("Please enter a valid http(s) URL", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isDownloading) return;

            await StartDownloadAsync(url, isChannel: false);
        }

        private async void ChannelDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (!IsValidHttpUrl(url))
            {
                MessageBox.Show("Please enter a valid http(s) channel URL", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isDownloading) return;

            var result = MessageBox.Show(
                "This will download ALL videos from the channel.\n\n" +
                "This may take a very long time and use significant storage space.\n\n" +
                "Continue?",
                "Download Entire Channel",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
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

            DownloadButton.Visibility = Visibility.Collapsed;
            ChannelDownloadButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            ShowInFolderButton.Visibility = Visibility.Collapsed;
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
                    MessageBox.Show(validationError, "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    ShowInFolderButton.Visibility = Visibility.Visible;
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

                DownloadButton.Visibility = Visibility.Visible;
                ChannelDownloadButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
            }
        }

        private static string NormalizeChannelUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;

            string host = uri.Host.ToLowerInvariant();
            if (!host.EndsWith("youtube.com")) return url;

            string path = uri.AbsolutePath;
            bool isChannelPath =
                path.StartsWith("/@") ||
                path.StartsWith("/c/") ||
                path.StartsWith("/channel/") ||
                path.StartsWith("/user/");

            if (!isChannelPath) return url;

            string trimmed = path.TrimEnd('/');
            if (!trimmed.EndsWith("/videos"))
            {
                trimmed += "/videos";
            }

            var builder = new UriBuilder(uri) { Path = trimmed };
            return builder.Uri.ToString();
        }

        private List<string>? BuildDownloadArguments(string url, bool isChannel, out string? error)
        {
            error = null;
            var args = new List<string>();

            args.Add("--ffmpeg-location");
            args.Add(ToolsExtractor.FfmpegDirectory);

            if (File.Exists(_cookiesPath))
            {
                args.Add("--cookies");
                args.Add(_cookiesPath);
            }

            args.Add("--newline");
            args.Add("--progress");

            bool isMp3 = Mp3Radio.IsChecked == true;
            string outputDir = OutputDirTextBox.Text;
            string filename = FilenameTextBox.Text.Trim();

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
                args.Add("--audio-format");
                args.Add("mp3");
                args.Add("--audio-quality");
                args.Add("0");
            }
            else
            {
                string quality = (QualityComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Best";
                string formatSpec = quality == "Best"
                    ? "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best"
                    : $"bestvideo[height<={quality.Replace("p", "")}][ext=mp4]+bestaudio[ext=m4a]/best[height<={quality.Replace("p", "")}][ext=mp4]/best";

                args.Add("-f");
                args.Add(formatSpec);
                args.Add("--merge-output-format");
                args.Add("mp4");
            }

            args.Add("-o");
            args.Add(outputTemplate);

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

            // Register kill on cancellation so we don't poll.
            using var killReg = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch { /* process already gone */ }
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
                // Always drain readers so we don't orphan them, even on cancel.
                try { await Task.WhenAll(outputTask, errorTask); }
                catch { /* readers complete on stream close */ }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (process.ExitCode != 0)
            {
                throw new Exception($"yt-dlp exited with code {process.ExitCode}");
            }
        }

        // [download]  23.4% of  5.32MiB at 1.21MiB/s ETA 00:04
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

                Dispatcher.BeginInvoke(new Action(() =>
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
                }));
                // Don't push the noisy per-tick progress lines into the log.
                return;
            }

            if (line.Contains("has already been downloaded"))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DownloadProgressBar.Value = 100;
                    ProgressText.Text = "Already downloaded";
                }));
            }

            Log(line);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isDownloading)
            {
                var result = MessageBox.Show(
                    "A download is in progress. Cancel and exit?",
                    "Download in Progress",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                _cancellationTokenSource?.Cancel();
            }

            base.OnClosing(e);
        }
    }
}
