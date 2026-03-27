using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace YouTubeDownloader
{
    public partial class MainWindow : Window
    {
        private readonly string _outputDirectory;
        private readonly string _cookiesPath;

        private Process? _currentProcess;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isDownloading;

        public MainWindow()
        {
            InitializeComponent();

            // Extract embedded tools on first run
            try
            {
                ToolsExtractor.ExtractTools();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to extract tools: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Set up paths - videos folder next to exe, cookies next to exe
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _outputDirectory = Path.Combine(exeDir, "videos");
            _cookiesPath = Path.Combine(exeDir, "cookies.txt");

            // Ensure output directory exists
            Directory.CreateDirectory(_outputDirectory);

            // Set default output directory
            OutputDirTextBox.Text = _outputDirectory;

            // Log startup info
            Log("YouTube Downloader started");
            Log($"Tools location: {ToolsExtractor.ToolsDirectory}");

            if (File.Exists(_cookiesPath))
            {
                Log("Cookies file found - age-restricted videos supported.");
            }

            // Auto-update yt-dlp in background
            Task.Run(() => UpdateYtDlp());
        }

        private async Task UpdateYtDlp()
        {
            try
            {
                await Task.Run(() =>
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ToolsExtractor.YtDlpPath,
                        Arguments = "-U",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null) return;

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        Dispatcher.Invoke(() => Log($"yt-dlp update: {output.Trim()}"));
                    }
                });
            }
            catch
            {
                // Silently fail if update doesn't work
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogListBox.Items.Add(message);
                LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
            });
        }

        private void ClearLog()
        {
            Dispatcher.Invoke(() => LogListBox.Items.Clear());
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
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a YouTube URL", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ToolsExtractor.YtDlpPath,
                        Arguments = $"--dump-json --no-playlist \"{url}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    if (File.Exists(_cookiesPath))
                    {
                        startInfo.Arguments = $"--cookies \"{_cookiesPath}\" " + startInfo.Arguments;
                    }

                    using var process = Process.Start(startInfo);
                    if (process == null) return null;

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return JObject.Parse(output);
                    }
                    else
                    {
                        Log($"Error: {error}");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Exception: {ex.Message}");
                    return null;
                }
            });
        }

        private async void DisplayVideoInfo(JObject info)
        {
            VideoInfoPanel.Visibility = Visibility.Visible;

            string title = info["title"]?.ToString() ?? "Unknown";
            if (title.Length > 80)
                title = title.Substring(0, 77) + "...";
            VideoTitleText.Text = title;

            string channel = info["uploader"]?.ToString() ?? "Unknown";
            ChannelText.Text = $"Channel: {channel}";

            int duration = info["duration"]?.Value<int>() ?? 0;
            int minutes = duration / 60;
            int seconds = duration % 60;
            DurationText.Text = $"Duration: {minutes}:{seconds:D2}";

            Log($"Found: {info["title"]}");

            // Load thumbnail
            string? thumbnailUrl = info["thumbnail"]?.ToString();
            if (!string.IsNullOrEmpty(thumbnailUrl))
            {
                await LoadThumbnailAsync(thumbnailUrl);
            }
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
                Process.Start("explorer.exe", path);
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
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a YouTube URL", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isDownloading) return;

            await StartDownloadAsync(url, isChannel: false);
        }

        private async void ChannelDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a YouTube channel URL", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        private async Task StartDownloadAsync(string url, bool isChannel)
        {
            _isDownloading = true;
            _cancellationTokenSource = new CancellationTokenSource();

            // Update UI
            DownloadButton.Visibility = Visibility.Collapsed;
            ChannelDownloadButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            DownloadProgressBar.Value = 0;
            ClearLog();

            try
            {
                string args = BuildDownloadArguments(url, isChannel);
                Log($"Starting download...");
                Log($"Command: yt-dlp {args}");

                await RunDownloadProcessAsync(args, _cancellationTokenSource.Token);

                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    DownloadProgressBar.Value = 100;
                    ProgressText.Text = "Download complete!";
                    Log("\n Download completed successfully!");
                    MessageBox.Show("Download completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
                _currentProcess = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                // Restore UI
                DownloadButton.Visibility = Visibility.Visible;
                ChannelDownloadButton.Visibility = Visibility.Visible;
                CancelButton.Visibility = Visibility.Collapsed;
            }
        }

        private string NormalizeChannelUrl(string url)
        {
            // Normalize channel URL to download all videos
            // Handles: @username, /c/name, /channel/ID, /user/name
            if (url.Contains("youtube.com/@") ||
                url.Contains("youtube.com/c/") ||
                url.Contains("youtube.com/channel/") ||
                url.Contains("youtube.com/user/"))
            {
                // Remove trailing slash
                url = url.TrimEnd('/');

                // If URL doesn't end with /videos, add it
                if (!url.EndsWith("/videos"))
                {
                    url += "/videos";
                }
            }

            return url;
        }

        private string BuildDownloadArguments(string url, bool isChannel)
        {
            var args = new System.Text.StringBuilder();

            // FFmpeg location
            args.Append($"--ffmpeg-location \"{ToolsExtractor.FfmpegDirectory}\" ");

            // Cookies
            if (File.Exists(_cookiesPath))
            {
                args.Append($"--cookies \"{_cookiesPath}\" ");
            }

            // Progress output
            args.Append("--newline --progress ");

            // Format selection
            bool isMp3 = Mp3Radio.IsChecked == true;
            string outputDir = OutputDirTextBox.Text;
            string filename = FilenameTextBox.Text.Trim();

            if (isMp3)
            {
                args.Append("-x --audio-format mp3 --audio-quality 0 ");

                if (!string.IsNullOrEmpty(filename) && !isChannel)
                {
                    args.Append($"-o \"{Path.Combine(outputDir, filename + ".%(ext)s")}\" ");
                }
                else
                {
                    args.Append($"-o \"{Path.Combine(outputDir, "%(title)s.%(ext)s")}\" ");
                }
            }
            else
            {
                // Video format
                string quality = (QualityComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Best";

                if (quality == "Best")
                {
                    args.Append("-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\" ");
                }
                else
                {
                    string height = quality.Replace("p", "");
                    args.Append($"-f \"bestvideo[height<={height}][ext=mp4]+bestaudio[ext=m4a]/best[height<={height}][ext=mp4]/best\" ");
                }

                args.Append("--merge-output-format mp4 ");

                if (!string.IsNullOrEmpty(filename) && !isChannel)
                {
                    args.Append($"-o \"{Path.Combine(outputDir, filename + ".%(ext)s")}\" ");
                }
                else
                {
                    args.Append($"-o \"{Path.Combine(outputDir, "%(title)s.%(ext)s")}\" ");
                }
            }

            // Normalize channel URL if downloading entire channel
            if (isChannel)
            {
                url = NormalizeChannelUrl(url);
                args.Append("--yes-playlist ");
            }
            else
            {
                args.Append("--no-playlist ");
            }

            args.Append($"\"{url}\"");

            return args.ToString();
        }

        private async Task RunDownloadProcessAsync(string arguments, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ToolsExtractor.YtDlpPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _currentProcess = new Process { StartInfo = startInfo };
            _currentProcess.Start();

            // Read output asynchronously
            var outputTask = Task.Run(async () =>
            {
                while (!_currentProcess.StandardOutput.EndOfStream)
                {
                    string? line = await _currentProcess.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        ProcessOutputLine(line);
                    }
                }
            }, cancellationToken);

            var errorTask = Task.Run(async () =>
            {
                while (!_currentProcess.StandardError.EndOfStream)
                {
                    string? line = await _currentProcess.StandardError.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        Log(line);
                    }
                }
            }, cancellationToken);

            // Wait for process with cancellation support
            while (!_currentProcess.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        _currentProcess.Kill(true);
                    }
                    catch { }
                    throw new OperationCanceledException();
                }
                await Task.Delay(100, cancellationToken);
            }

            await Task.WhenAll(outputTask, errorTask);

            if (_currentProcess.ExitCode != 0 && !cancellationToken.IsCancellationRequested)
            {
                throw new Exception($"yt-dlp exited with code {_currentProcess.ExitCode}");
            }
        }

        private void ProcessOutputLine(string line)
        {
            Log(line);

            // Parse progress percentage
            var match = Regex.Match(line, @"(\d+\.?\d*)%");
            if (match.Success && double.TryParse(match.Groups[1].Value, out double progress))
            {
                Dispatcher.Invoke(() =>
                {
                    DownloadProgressBar.Value = progress;
                    ProgressText.Text = $"Downloading: {progress:F1}%";
                });
            }

            // Check for completion
            if (line.Contains("[download] 100%") || line.Contains("has already been downloaded"))
            {
                Dispatcher.Invoke(() => DownloadProgressBar.Value = 100);
            }
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
