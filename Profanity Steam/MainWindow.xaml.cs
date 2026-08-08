using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Profanity_Steam
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; set; }
        private readonly string _gamesDirectory = @"C:\Users\phili\AppData\Local\Profanity\Steam\Games";
        private readonly string _steamLuaDirectory = @"C:\Program Files (x86)\Steam\config\lua";
        private readonly string _defaultSteamPath = @"C:\Program Files (x86)\Steam";
        private readonly List<string> _allLibraryPaths = new List<string>();
        private bool _isMonitoring;
        private bool _isPrompting;
        private string _activeAppId;

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            Loaded += MainWindow_Loaded;
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

            var source = e.OriginalSource as System.Windows.DependencyObject;
            while (source != null)
            {
                if (source is System.Windows.Controls.Primitives.ButtonBase ||
                    source is System.Windows.Controls.TextBox ||
                    source is System.Windows.Controls.ComboBox)
                {
                    return;
                }
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }

            DragMove();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(_gamesDirectory);
            FindAllLibraryPaths();
            await LoadSavedGamesAsync();
        }

        private async Task LoadSavedGamesAsync()
        {
            ViewModel.MyGames.Clear();
            if (!Directory.Exists(_gamesDirectory)) return;

            var files = Directory.GetFiles(_gamesDirectory);
            foreach (var file in files)
            {
                if (file.EndsWith(".lua") || file.EndsWith(".manifest") || file.EndsWith(".disabled"))
                {
                    string content = File.ReadAllText(file);
                    string fileName = Path.GetFileNameWithoutExtension(file);

                    if (fileName.EndsWith(".lua") || fileName.EndsWith(".manifest"))
                    {
                        fileName = Path.GetFileNameWithoutExtension(fileName);
                    }

                    string detectedAppId = "Unknown";
                    string detectedName = "Custom Game";
                    bool isSelected = !file.EndsWith(".disabled");

                    if (Regex.IsMatch(fileName, @"^\d+$"))
                    {
                        detectedAppId = fileName;
                    }
                    else
                    {
                        var idMatch = Regex.Match(content, @"(?i)appid\s*=\s*(\d+)");
                        if (idMatch.Success) detectedAppId = idMatch.Groups[1].Value;
                    }

                    var nameMatch = Regex.Match(content, @"(?i)name\s*=\s*['""]([^'""]+)['""]");
                    if (nameMatch.Success)
                    {
                        detectedName = nameMatch.Groups[1].Value;
                    }
                    else if (detectedAppId != "Unknown")
                    {
                        detectedName = await FetchGameNameAsync(detectedAppId);
                    }

                    ViewModel.MyGames.Add(new GameItem
                    {
                        AppId = detectedAppId,
                        Name = detectedName,
                        ImagePath = detectedAppId != "Unknown" ? $"https://steamcdn-a.akamaihd.net/steam/apps/{detectedAppId}/header.jpg" : "",
                        FilePath = file,
                        IsSelected = isSelected
                    });
                }
            }
        }

        private async void AddGameButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Select Game Lua or Manifest Script",
                Filter = "Lua & Manifest Files (*.lua;*.manifest)|*.lua;*.manifest|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string filePath = openFileDialog.FileName;
                    string content = File.ReadAllText(filePath);
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string extension = Path.GetExtension(filePath);

                    string detectedAppId = "Unknown";
                    string detectedName = "Custom Game";

                    if (Regex.IsMatch(fileName, @"^\d+$"))
                    {
                        detectedAppId = fileName;
                    }
                    else
                    {
                        var idMatch = Regex.Match(content, @"(?i)appid\s*=\s*(\d+)");
                        if (idMatch.Success) detectedAppId = idMatch.Groups[1].Value;
                    }

                    var nameMatch = Regex.Match(content, @"(?i)name\s*=\s*['""]([^'""]+)['""]");
                    if (nameMatch.Success)
                    {
                        detectedName = nameMatch.Groups[1].Value;
                    }
                    else if (detectedAppId != "Unknown")
                    {
                        detectedName = await FetchGameNameAsync(detectedAppId);
                    }

                    if (detectedAppId == "Unknown")
                    {
                        MessageBox.Show("Could not detect an App ID from the selected file. Adding as Custom Game.", "Profanity", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    Directory.CreateDirectory(_gamesDirectory);
                    string newFileName = detectedAppId != "Unknown" ? $"{detectedAppId}{extension}.disabled" : $"{Guid.NewGuid()}{extension}.disabled";
                    string destPath = Path.Combine(_gamesDirectory, newFileName);

                    File.Copy(filePath, destPath, true);

                    ViewModel.MyGames.Add(new GameItem
                    {
                        AppId = detectedAppId,
                        Name = detectedName,
                        ImagePath = detectedAppId != "Unknown" ? $"https://steamcdn-a.akamaihd.net/steam/apps/{detectedAppId}/header.jpg" : "",
                        FilePath = destPath,
                        IsSelected = false
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading script: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task<string> FetchGameNameAsync(string appId)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string json = await client.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}");
                    var match = Regex.Match(json, @"""name"":\s*""([^""]+)""");
                    if (match.Success)
                    {
                        return Regex.Unescape(match.Groups[1].Value);
                    }
                }
            }
            catch { }

            return $"Steam App {appId}";
        }

        private void RemoveGameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is GameItem game)
            {
                ViewModel.MyGames.Remove(game);

                if (!string.IsNullOrEmpty(game.FilePath) && File.Exists(game.FilePath))
                {
                    try
                    {
                        File.Delete(game.FilePath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete file from storage: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void GetGamesButton_Click(object sender, RoutedEventArgs e)
        {
            ApplySelectionAndRestart("Applying selected games and restarting Steam...");
        }

        private void RevokeButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var game in ViewModel.MyGames)
            {
                game.IsSelected = false;
            }

            ApplySelectionAndRestart("Revoking all games and restarting Steam...");
        }

        private void ApplySelectionAndRestart(string message)
        {
            try
            {
                Directory.CreateDirectory(_gamesDirectory);

                if (Directory.Exists(_steamLuaDirectory))
                {
                    var existingFiles = Directory.GetFiles(_steamLuaDirectory);
                    foreach (var file in existingFiles)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                else
                {
                    Directory.CreateDirectory(_steamLuaDirectory);
                }

                foreach (var game in ViewModel.MyGames)
                {
                    if (string.IsNullOrEmpty(game.FilePath) || !File.Exists(game.FilePath)) continue;

                    string fileName = Path.GetFileName(game.FilePath);

                    if (game.IsSelected)
                    {
                        if (fileName.EndsWith(".disabled"))
                        {
                            string newName = fileName.Substring(0, fileName.Length - ".disabled".Length);
                            string newPath = Path.Combine(_gamesDirectory, newName);
                            File.Move(game.FilePath, newPath);
                            game.FilePath = newPath;
                            fileName = newName;
                        }

                        string destSteamPath = Path.Combine(_steamLuaDirectory, fileName);
                        File.Copy(game.FilePath, destSteamPath, true);
                    }
                    else
                    {
                        if (!fileName.EndsWith(".disabled"))
                        {
                            string newPath = game.FilePath + ".disabled";
                            File.Move(game.FilePath, newPath);
                            game.FilePath = newPath;
                        }
                    }
                }

                foreach (var process in Process.GetProcessesByName("steam"))
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }

                string steamPath = null;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        steamPath = key.GetValue("SteamExe") as string;
                    }
                }

                if (!string.IsNullOrEmpty(steamPath) && File.Exists(steamPath))
                {
                    Process.Start(steamPath);
                    MessageBox.Show(message, "Profanity", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Steam process closed, but executable path could not be located in registry.", "Profanity", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FindGamesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://generator.ryuu.lol/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open browser: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FindAllLibraryPaths()
        {
            string defaultApps = Path.Combine(_defaultSteamPath, "steamapps");
            if (Directory.Exists(defaultApps)) _allLibraryPaths.Add(defaultApps);

            string vdfPath = Path.Combine(defaultApps, "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    string content = File.ReadAllText(vdfPath);
                    var matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");
                    foreach (Match m in matches)
                    {
                        string rawPath = Regex.Unescape(m.Groups[1].Value);
                        string cleanPath = rawPath.Replace("\\\\", "\\");
                        string appsPath = Path.Combine(cleanPath, "steamapps");

                        if (Directory.Exists(appsPath) && !_allLibraryPaths.Contains(appsPath, StringComparer.OrdinalIgnoreCase))
                        {
                            _allLibraryPaths.Add(appsPath);
                        }
                    }
                }
                catch { }
            }
        }

        private async void TabAfk_Checked(object sender, RoutedEventArgs e)
        {
            await CheckForActiveDownloadAsync();
        }

        private Task CheckForActiveDownloadAsync()
        {
            if (_isMonitoring) return Task.CompletedTask;

            var downloadInfo = GetActiveDownloadInfo();
            if (downloadInfo != null)
            {
                _isPrompting = true;
                _activeAppId = downloadInfo.Item2;
                AfkStatusText.Text = $"Do you want to monitor '{downloadInfo.Item1}'?";

                try
                {
                    AfkGameImage.Source = new BitmapImage(new Uri($"https://cdn.cloudflare.steamstatic.com/steam/apps/{_activeAppId}/header.jpg"));
                    AfkGameFrame.Visibility = Visibility.Visible;
                }
                catch { AfkGameFrame.Visibility = Visibility.Collapsed; }

                ActionBtn.Content = "Yes";
                CancelBtn.Content = "No";
                CancelBtn.Visibility = Visibility.Visible;
                PowerActionBox.IsEnabled = true;
                FallbackTimeBox.IsEnabled = true;
                SingleGameToggle.IsEnabled = true;
            }
            else
            {
                _isPrompting = false;
                AfkStatusText.Text = "Ready to monitor Steam downloads.";
                AfkGameFrame.Visibility = Visibility.Collapsed;
                ActionBtn.Content = "Start Monitoring";
                CancelBtn.Content = "Abort";
                CancelBtn.Visibility = Visibility.Collapsed;
            }

            return Task.CompletedTask;
        }

        private async void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _isPrompting = false;

            ActionBtn.Visibility = Visibility.Collapsed;
            CancelBtn.Content = "Abort";
            CancelBtn.Visibility = Visibility.Visible;
            PowerActionBox.IsEnabled = false;
            FallbackTimeBox.IsEnabled = false;
            SingleGameToggle.IsEnabled = false;
            AfkProgressBar.IsIndeterminate = true;

            await MonitorDownloadAsync();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CountdownOverlay.Visibility = Visibility.Collapsed;

            if (_isPrompting)
            {
                _isPrompting = false;
                AfkStatusText.Text = "Ready to monitor Steam downloads.";
                AfkGameFrame.Visibility = Visibility.Collapsed;
                ActionBtn.Content = "Start Monitoring";
                CancelBtn.Visibility = Visibility.Collapsed;
                return;
            }

            _isMonitoring = false;
            AfkStatusText.Text = "Monitoring aborted. Action cancelled.";
            AfkProgressBar.IsIndeterminate = false;
            ActionBtn.Content = "Start Monitoring";
            ActionBtn.Visibility = Visibility.Visible;
            CancelBtn.Visibility = Visibility.Collapsed;
            PowerActionBox.IsEnabled = true;
            FallbackTimeBox.IsEnabled = true;
            SingleGameToggle.IsEnabled = true;
            Process.Start(new ProcessStartInfo("shutdown.exe", "/a") { CreateNoWindow = true, UseShellExecute = false });
        }

        private async Task MonitorDownloadAsync()
        {
            bool stopAfterSingleGame = false;
            string targetAppId = null;
            DateTime startTime = DateTime.Now;
            double fallbackHours = 0;

            Application.Current.Dispatcher.Invoke(() =>
            {
                stopAfterSingleGame = SingleGameToggle.IsChecked == true;
                targetAppId = _activeAppId;

                string fallbackSelection = ((ComboBoxItem)FallbackTimeBox.SelectedItem).Content.ToString();
                if (fallbackSelection.Contains("1")) fallbackHours = 1;
                else if (fallbackSelection.Contains("3")) fallbackHours = 3;
                else if (fallbackSelection.Contains("6")) fallbackHours = 6;
            });

            await Task.Run(async () =>
            {
                while (_isMonitoring)
                {
                    if (fallbackHours > 0 && (DateTime.Now - startTime).TotalHours >= fallbackHours)
                    {
                        break;
                    }

                    if (stopAfterSingleGame && !string.IsNullOrEmpty(targetAppId))
                    {
                        if (!IsGameDownloading(targetAppId))
                        {
                            await Task.Delay(5000);
                            if (!IsGameDownloading(targetAppId)) break;
                        }
                    }
                    else
                    {
                        if (!HasAnyActiveDownloads())
                        {
                            await Task.Delay(5000);
                            if (!HasAnyActiveDownloads()) break;
                        }
                    }

                    var info = GetActiveDownloadInfo();
                    if (info != null)
                        Application.Current.Dispatcher.Invoke(() => AfkStatusText.Text = $"Downloading: {info.Item1}");
                    else
                        Application.Current.Dispatcher.Invoke(() => AfkStatusText.Text = "Verifying/Allocating...");

                    await Task.Delay(3000);
                }

                if (!_isMonitoring) return;

                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    AfkProgressBar.IsIndeterminate = false;
                    AfkStatusText.Text = "Action Triggered.";

                    string action = ((ComboBoxItem)PowerActionBox.SelectedItem).Content.ToString();
                    CountdownActionText.Text = action == "Shutdown" ? "Shutting down in..." :
                                               action == "Restart" ? "Restarting in..." : "Going to sleep in...";

                    CountdownOverlay.Visibility = Visibility.Visible;

                    for (int i = 30; i > 0; i--)
                    {
                        if (!_isMonitoring) return;
                        CountdownTimerText.Text = i.ToString();
                        await Task.Delay(1000);
                    }

                    if (_isMonitoring) ExecutePowerAction(action);
                });
            });
        }

        private void ExecutePowerAction(string action)
        {
            _isMonitoring = false;
            CountdownOverlay.Visibility = Visibility.Collapsed;
            ActionBtn.Visibility = Visibility.Visible;
            CancelBtn.Visibility = Visibility.Collapsed;
            PowerActionBox.IsEnabled = true;
            FallbackTimeBox.IsEnabled = true;
            SingleGameToggle.IsEnabled = true;

            if (action == "Shutdown")
                Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
            else if (action == "Restart")
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
            else if (action == "Sleep")
                Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { CreateNoWindow = true, UseShellExecute = false });
        }

        private bool HasAnyActiveDownloads()
        {
            foreach (var lib in _allLibraryPaths)
            {
                string dlFolder = Path.Combine(lib, "downloading");
                if (Directory.Exists(dlFolder) && Directory.EnumerateFileSystemEntries(dlFolder).Any())
                    return true;
            }
            return false;
        }

        private bool IsGameDownloading(string appId)
        {
            foreach (var lib in _allLibraryPaths)
            {
                if (Directory.Exists(Path.Combine(lib, "downloading", appId))) return true;

                string manifest = Path.Combine(lib, $"appmanifest_{appId}.acf");
                if (File.Exists(manifest))
                {
                    try
                    {
                        string content = File.ReadAllText(manifest);
                        string toDl = Regex.Match(content, "\"BytesToDownload\"\\s+\"(\\d+)\"").Groups[1].Value;
                        string dl = Regex.Match(content, "\"BytesDownloaded\"\\s+\"(\\d+)\"").Groups[1].Value;

                        if (long.TryParse(toDl, out long total) && long.TryParse(dl, out long down) && total > 0 && down < total)
                            return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private Tuple<string, string, long, long> GetActiveDownloadInfo()
        {
            foreach (var lib in _allLibraryPaths)
            {
                if (!Directory.Exists(lib)) continue;
                var manifests = Directory.GetFiles(lib, "appmanifest_*.acf");
                foreach (var m in manifests)
                {
                    try
                    {
                        string content = File.ReadAllText(m);
                        string toDl = Regex.Match(content, "\"BytesToDownload\"\\s+\"(\\d+)\"").Groups[1].Value;
                        string dl = Regex.Match(content, "\"BytesDownloaded\"\\s+\"(\\d+)\"").Groups[1].Value;

                        if (long.TryParse(toDl, out long total) && long.TryParse(dl, out long downloaded) && total > 0 && downloaded < total)
                        {
                            string appId = Regex.Match(content, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                            string name = Regex.Match(content, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                            return new Tuple<string, string, long, long>(Regex.Unescape(name), appId, total, downloaded);
                        }
                    }
                    catch { }
                }
            }
            return null;
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private bool _runInBackground;
        private bool _runOnStartup;
        private bool _enableSpoofing = true;
        private string _currentUserName;
        private string _currentDate;

        public ObservableCollection<GameItem> MyGames { get; set; } = new ObservableCollection<GameItem>();

        public MainViewModel()
        {
            CurrentUserName = Environment.UserName;
            CurrentDate = DateTime.Now.ToString("MMMM dd, yyyy");
        }

        public string CurrentUserName { get => _currentUserName; set { _currentUserName = value; OnPropertyChanged(); } }
        public string CurrentDate { get => _currentDate; set { _currentDate = value; OnPropertyChanged(); } }

        public bool RunInBackground { get => _runInBackground; set { _runInBackground = value; OnPropertyChanged(); } }
        public bool RunOnStartup { get => _runOnStartup; set { _runOnStartup = value; OnPropertyChanged(); } }
        public bool EnableSpoofing { get => _enableSpoofing; set { _enableSpoofing = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GameItem : INotifyPropertyChanged
    {
        private string _name;
        private string _appId;
        private string _imagePath;
        private string _filePath;
        private bool _isSelected = true;

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public string AppId { get => _appId; set { _appId = value; OnPropertyChanged(); } }
        public string ImagePath { get => _imagePath; set { _imagePath = value; OnPropertyChanged(); } }
        public string FilePath { get => _filePath; set { _filePath = value; OnPropertyChanged(); } }
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}