using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Profanity_Steam
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; set; }
        private readonly string _gamesDirectory = @"C:\Users\phili\AppData\Local\Profanity\Steam\Games";
        private readonly string _steamLuaDirectory = @"C:\Program Files (x86)\Steam\config\lua";

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            Loaded += MainWindow_Loaded;
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox ||
                e.OriginalSource is System.Windows.Controls.CheckBox ||
                e.OriginalSource is System.Windows.Controls.Primitives.ToggleButton ||
                e.OriginalSource is System.Windows.Controls.Button)
            {
                return;
            }

            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(_gamesDirectory);
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