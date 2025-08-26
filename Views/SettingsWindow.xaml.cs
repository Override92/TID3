using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Microsoft.Win32;
using TID3.Models;
using TID3.Services;
using TID3.Utils;

namespace TID3.Views
{
    // UI Model for Cover Art Source configuration
    public class CoverArtSourceViewModel : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private int _priority;

        public CoverSourceType SourceType { get; set; }
        public string DisplayName { get; set; } = "";
        
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }
        
        public int Priority
        {
            get => _priority;
            set { _priority = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private AppSettings _currentSettings = null!;
        private ObservableCollection<CoverArtSourceViewModel> _coverArtSources = new();

        public SettingsWindow()
        {
            InitializeComponent();
            LoadVersion();
            LoadSettings();
            InitializeCoverArtSources();
        }

        private void LoadVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                var fileVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
                
                if (version != null)
                {
                    // Show full version with build number
                    VersionTextBlock.Text = $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                    
                    // Add additional info if available
                    if (!string.IsNullOrEmpty(fileVersion.FileVersion))
                    {
                        VersionTextBlock.Text += $" (Build #{version.Revision})";
                    }
                }
                else
                {
                    VersionTextBlock.Text = "Version Unknown";
                }
            }
            catch
            {
                VersionTextBlock.Text = "Version 0.1.0 (Beta)";
            }
        }

        private void LoadSettings()
        {
            _currentSettings = SettingsManager.LoadSettings();

            // API Configuration
            MusicBrainzUserAgentTextBox.Text = _currentSettings.MusicBrainzUserAgent;
            DiscogsApiKeyPasswordBox.Password = _currentSettings.DiscogsApiKey;
            DiscogsSecretPasswordBox.Password = _currentSettings.DiscogsSecret;
            AcoustIdApiKeyPasswordBox.Password = _currentSettings.AcoustIdApiKey;

            // Cover Art Settings
            LastFmApiKeyPasswordBox.Password = _currentSettings.CoverArtSettings.LastFmApiKey;
            SpotifyClientIdPasswordBox.Password = _currentSettings.CoverArtSettings.SpotifyClientId;
            SpotifyClientSecretPasswordBox.Password = _currentSettings.CoverArtSettings.SpotifyClientSecret;

            // File Processing
            AutoSaveCheckBox.IsChecked = _currentSettings.AutoSave;
            CreateBackupCheckBox.IsChecked = _currentSettings.CreateBackup;
            IncludeSubdirectoriesCheckBox.IsChecked = _currentSettings.IncludeSubdirectories;
            SupportedExtensionsTextBox.Text = string.Join(", ", _currentSettings.SupportedExtensions);
            MaxConcurrentOperationsSlider.Value = _currentSettings.MaxConcurrentOperations;

            // User Interface
            ThemeComboBox.SelectedIndex = (int)_currentSettings.Theme;
            LanguageComboBox.SelectedIndex = (int)_currentSettings.Language;
            ShowFileExtensionsCheckBox.IsChecked = _currentSettings.ShowFileExtensions;
            ShowToolTipsCheckBox.IsChecked = _currentSettings.ShowToolTips;
            MinimizeToTrayCheckBox.IsChecked = _currentSettings.MinimizeToTray;
            FontSizeSlider.Value = _currentSettings.FontSize;

            // Advanced Settings
            EnableLoggingCheckBox.IsChecked = _currentSettings.EnableLogging;
            CheckForUpdatesCheckBox.IsChecked = _currentSettings.CheckForUpdates;
            SendUsageStatsCheckBox.IsChecked = _currentSettings.SendUsageStats;
            CacheDirectoryTextBox.Text = _currentSettings.CacheDirectory;
        }

        private void SaveSettings()
        {
            // API Configuration
            _currentSettings.MusicBrainzUserAgent = MusicBrainzUserAgentTextBox.Text.Trim();
            _currentSettings.DiscogsApiKey = DiscogsApiKeyPasswordBox.Password;
            _currentSettings.DiscogsSecret = DiscogsSecretPasswordBox.Password;
            _currentSettings.AcoustIdApiKey = AcoustIdApiKeyPasswordBox.Password;

            // Cover Art Settings
            _currentSettings.CoverArtSettings.LastFmApiKey = LastFmApiKeyPasswordBox.Password;
            _currentSettings.CoverArtSettings.SpotifyClientId = SpotifyClientIdPasswordBox.Password;
            _currentSettings.CoverArtSettings.SpotifyClientSecret = SpotifyClientSecretPasswordBox.Password;
            SaveCoverArtSourceSettings();

            // File Processing
            _currentSettings.AutoSave = AutoSaveCheckBox.IsChecked ?? false;
            _currentSettings.CreateBackup = CreateBackupCheckBox.IsChecked ?? false;
            _currentSettings.IncludeSubdirectories = IncludeSubdirectoriesCheckBox.IsChecked ?? true;
            _currentSettings.SupportedExtensions = [.. SupportedExtensionsTextBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.Trim().ToLowerInvariant())];
            _currentSettings.MaxConcurrentOperations = (int)MaxConcurrentOperationsSlider.Value;

            // User Interface
            _currentSettings.Theme = (AppTheme)ThemeComboBox.SelectedIndex;
            _currentSettings.Language = (AppLanguage)LanguageComboBox.SelectedIndex;
            _currentSettings.ShowFileExtensions = ShowFileExtensionsCheckBox.IsChecked ?? true;
            _currentSettings.ShowToolTips = ShowToolTipsCheckBox.IsChecked ?? true;
            _currentSettings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked ?? false;
            _currentSettings.FontSize = (int)FontSizeSlider.Value;

            // Advanced Settings
            _currentSettings.EnableLogging = EnableLoggingCheckBox.IsChecked ?? false;
            _currentSettings.CheckForUpdates = CheckForUpdatesCheckBox.IsChecked ?? true;
            _currentSettings.SendUsageStats = SendUsageStatsCheckBox.IsChecked ?? false;
            _currentSettings.CacheDirectory = CacheDirectoryTextBox.Text.Trim();
        }

        private async void TestDiscogs_Click(object sender, RoutedEventArgs e)
        {
            DiscogsTestStatus.Text = "Testing connection...";
            DiscogsTestStatus.Foreground = System.Windows.Media.Brushes.Orange;

            try
            {
                var apiKey = DiscogsApiKeyPasswordBox.Password;
                var secret = DiscogsSecretPasswordBox.Password;

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    DiscogsTestStatus.Text = "API Key required";
                    DiscogsTestStatus.Foreground = System.Windows.Media.Brushes.Red;
                    return;
                }

                var client = HttpClientManager.General;
                var url = $"https://api.discogs.com/database/search?q=test&key={apiKey}&secret={secret}";

                var response = await client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    DiscogsTestStatus.Text = "✓ Connection successful";
                    DiscogsTestStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                else
                {
                    DiscogsTestStatus.Text = $"✗ Error: {response.StatusCode}";
                    DiscogsTestStatus.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                DiscogsTestStatus.Text = $"✗ Error: {ex.Message}";
                DiscogsTestStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private async void TestAcoustId_Click(object sender, RoutedEventArgs e)
        {
            AcoustIdTestStatus.Text = "Testing connection...";
            AcoustIdTestStatus.Foreground = System.Windows.Media.Brushes.Orange;

            try
            {
                var apiKey = AcoustIdApiKeyPasswordBox.Password;

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    AcoustIdTestStatus.Text = "API Key required";
                    AcoustIdTestStatus.Foreground = System.Windows.Media.Brushes.Red;
                    return;
                }

                var acoustIdService = new AcoustIdService(apiKey);
                var isConnectionValid = await acoustIdService.TestConnectionAsync();

                if (isConnectionValid)
                {
                    AcoustIdTestStatus.Text = "✓ Connection successful";
                    AcoustIdTestStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                else
                {
                    AcoustIdTestStatus.Text = "✗ Connection failed";
                    AcoustIdTestStatus.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                AcoustIdTestStatus.Text = $"✗ Error: {ex.Message}";
                AcoustIdTestStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void BrowseCacheDirectory_Click(object sender, RoutedEventArgs e)
        {
            using var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
            folderDialog.Description = "Select cache directory";
            folderDialog.SelectedPath = CacheDirectoryTextBox.Text;

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                CacheDirectoryTextBox.Text = folderDialog.SelectedPath;
            }
        }

        private void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear the cache? This will delete all cached data from online services.",
                "Clear Cache",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var cacheDir = CacheDirectoryTextBox.Text;
                    if (Directory.Exists(cacheDir))
                    {
                        Directory.Delete(cacheDir, true);
                        Directory.CreateDirectory(cacheDir);
                        MessageBox.Show("Cache cleared successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Cache directory does not exist.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error clearing cache: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to reset all settings to their default values? This cannot be undone.",
                "Reset Settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _currentSettings = new AppSettings(); // Create new default settings
                LoadSettings(); // Reload UI with defaults
                MessageBox.Show("Settings have been reset to defaults.", "Settings Reset", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveSettings();
                SettingsManager.SaveSettings(_currentSettings);

                MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings(); // Reload original settings
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PasswordBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Forward mouse wheel events to parent ScrollViewer
            e.Handled = true;
            var eventArg = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            var parent = ((Control)sender).Parent as UIElement;
            parent?.RaiseEvent(eventArg);
        }

        private void TextBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Forward mouse wheel events to parent ScrollViewer
            e.Handled = true;
            var eventArg = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            var parent = ((Control)sender).Parent as UIElement;
            parent?.RaiseEvent(eventArg);
        }

        private void Slider_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Forward mouse wheel events to parent ScrollViewer
            e.Handled = true;
            var eventArg = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            var parent = ((Control)sender).Parent as UIElement;
            parent?.RaiseEvent(eventArg);
        }

        private void ComboBox_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Forward mouse wheel events to parent ScrollViewer
            e.Handled = true;
            var eventArg = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            var parent = ((Control)sender).Parent as UIElement;
            parent?.RaiseEvent(eventArg);
        }

        private async void CheckForUpdatesNow_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var originalContent = button.Content;
            
            try
            {
                button.Content = "Checking...";
                button.IsEnabled = false;

                // Get the main window and call its update check method
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    var updateInfo = await mainWindow.CheckForUpdatesManually();
                    
                    if (updateInfo != null)
                    {
                        var message = $"A new version is available!\n\n" +
                                     $"Current Version: {_currentSettings.Version}\n" +
                                     $"Latest Version: {updateInfo.Version}\n" +
                                     $"Released: {updateInfo.PublishedAt:yyyy-MM-dd}\n\n" +
                                     $"Would you like to download the update?";

                        var result = MessageBox.Show(message, "Update Available", 
                            MessageBoxButton.YesNo, MessageBoxImage.Information);

                        if (result == MessageBoxResult.Yes)
                        {
                            try
                            {
                                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = updateInfo.DownloadUrl,
                                    UseShellExecute = true
                                });
                            }
                            catch
                            {
                                MessageBox.Show("Could not open the download page. Please visit the GitHub releases page manually.",
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("You are already running the latest version!", 
                            "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch
            {
                MessageBox.Show("Failed to check for updates. Please check your internet connection.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }

        private void InitializeCoverArtSources()
        {
            // Create view models for all cover art sources
            var sourceDisplayNames = new Dictionary<CoverSourceType, string>
            {
                { CoverSourceType.Local, "Local Files (Embedded & Folder)" },
                { CoverSourceType.Spotify, "Spotify (Official Artwork)" },
                { CoverSourceType.ITunes, "iTunes Store (Apple Music)" },
                { CoverSourceType.MusicBrainz, "MusicBrainz Cover Art Archive" },
                { CoverSourceType.Deezer, "Deezer (Music Streaming)" },
                { CoverSourceType.LastFm, "Last.fm (User Submitted)" },
                { CoverSourceType.Discogs, "Discogs (User Submitted)" }
            };

            foreach (var source in sourceDisplayNames)
            {
                var viewModel = new CoverArtSourceViewModel
                {
                    SourceType = source.Key,
                    DisplayName = source.Value,
                    Priority = _currentSettings.CoverArtSettings.GetPriority(source.Key),
                    IsEnabled = _currentSettings.CoverArtSettings.IsSourceEnabled(source.Key)
                };
                _coverArtSources.Add(viewModel);
            }

            // Sort by priority (highest first)
            var sortedSources = _coverArtSources.OrderByDescending(s => s.Priority).ToList();
            _coverArtSources.Clear();
            foreach (var source in sortedSources)
            {
                _coverArtSources.Add(source);
            }

            // Set ItemsSource
            CoverArtSourcesList.ItemsSource = _coverArtSources;
        }

        private void SaveCoverArtSourceSettings()
        {
            // Update settings from UI
            foreach (var source in _coverArtSources)
            {
                _currentSettings.CoverArtSettings.SetPriority(source.SourceType, source.Priority);
                
                // Update enable/disable settings
                switch (source.SourceType)
                {
                    case CoverSourceType.LastFm:
                        _currentSettings.CoverArtSettings.EnableLastFm = source.IsEnabled;
                        break;
                    case CoverSourceType.Spotify:
                        _currentSettings.CoverArtSettings.EnableSpotify = source.IsEnabled;
                        break;
                    case CoverSourceType.ITunes:
                        _currentSettings.CoverArtSettings.EnableITunes = source.IsEnabled;
                        break;
                    case CoverSourceType.Deezer:
                        _currentSettings.CoverArtSettings.EnableDeezer = source.IsEnabled;
                        break;
                    case CoverSourceType.MusicBrainz:
                        _currentSettings.CoverArtSettings.EnableMusicBrainz = source.IsEnabled;
                        break;
                    case CoverSourceType.Discogs:
                        _currentSettings.CoverArtSettings.EnableDiscogs = source.IsEnabled;
                        break;
                }
            }
        }

        private void MoveSourceUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CoverArtSourceViewModel source)
            {
                var currentIndex = _coverArtSources.IndexOf(source);
                if (currentIndex > 0)
                {
                    // Swap with item above
                    var itemAbove = _coverArtSources[currentIndex - 1];
                    var tempPriority = source.Priority;
                    source.Priority = itemAbove.Priority;
                    itemAbove.Priority = tempPriority;

                    // Move in collection
                    _coverArtSources.Move(currentIndex, currentIndex - 1);
                }
            }
        }

        private void MoveSourceDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CoverArtSourceViewModel source)
            {
                var currentIndex = _coverArtSources.IndexOf(source);
                if (currentIndex < _coverArtSources.Count - 1)
                {
                    // Swap with item below
                    var itemBelow = _coverArtSources[currentIndex + 1];
                    var tempPriority = source.Priority;
                    source.Priority = itemBelow.Priority;
                    itemBelow.Priority = tempPriority;

                    // Move in collection
                    _coverArtSources.Move(currentIndex, currentIndex + 1);
                }
            }
        }

        private async void TestLastFm_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var originalContent = button.Content;
            
            try
            {
                button.Content = "Testing...";
                button.IsEnabled = false;
                LastFmTestStatus.Text = "";

                var apiKey = LastFmApiKeyPasswordBox.Password.Trim();
                
                if (string.IsNullOrEmpty(apiKey))
                {
                    LastFmTestStatus.Text = "Please enter a Last.fm API key first";
                    LastFmTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                    return;
                }

                // Test with a well-known album
                var client = HttpClientManager.General;
                var lastFmService = new Services.LastFmService(client, apiKey);
                
                var coverUrl = await lastFmService.GetAlbumCoverAsync("The Beatles", "Abbey Road");
                
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    LastFmTestStatus.Text = "✓ Connection successful!";
                    LastFmTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Green
                }
                else
                {
                    LastFmTestStatus.Text = "✗ No results found (check API key)";
                    LastFmTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                }
            }
            catch (Exception ex)
            {
                LastFmTestStatus.Text = $"✗ Error: {ex.Message}";
                LastFmTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                System.Diagnostics.Debug.WriteLine($"Last.fm test error: {ex}");
            }
            finally
            {
                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }

        private async void TestSpotify_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var originalContent = button.Content;
            
            try
            {
                button.Content = "Testing...";
                button.IsEnabled = false;
                SpotifyTestStatus.Text = "";

                var clientId = SpotifyClientIdPasswordBox.Password.Trim();
                var clientSecret = SpotifyClientSecretPasswordBox.Password.Trim();
                
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    SpotifyTestStatus.Text = "Please enter both Client ID and Client Secret";
                    SpotifyTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                    return;
                }

                // Test Spotify Client Credentials flow
                var client = HttpClientManager.General;
                
                // Get access token using Client Credentials flow
                var authString = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                tokenRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
                tokenRequest.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

                var tokenResponse = await client.SendAsync(tokenRequest);
                
                if (tokenResponse.IsSuccessStatusCode)
                {
                    var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(tokenJson);
                    
                    if (doc.RootElement.TryGetProperty("access_token", out var tokenElement))
                    {
                        var accessToken = tokenElement.GetString();
                        
                        // Test search with the access token
                        using var searchRequest = new HttpRequestMessage(HttpMethod.Get, 
                            "https://api.spotify.com/v1/search?q=artist:The%20Beatles%20album:Abbey%20Road&type=album&limit=1");
                        searchRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                        
                        var searchResponse = await client.SendAsync(searchRequest);
                        
                        if (searchResponse.IsSuccessStatusCode)
                        {
                            var searchJson = await searchResponse.Content.ReadAsStringAsync();
                            using var searchDoc = System.Text.Json.JsonDocument.Parse(searchJson);
                            
                            if (searchDoc.RootElement.TryGetProperty("albums", out var albums) &&
                                albums.TryGetProperty("items", out var items) &&
                                items.GetArrayLength() > 0)
                            {
                                SpotifyTestStatus.Text = "✓ Connection successful!";
                                SpotifyTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Green
                            }
                            else
                            {
                                SpotifyTestStatus.Text = "✓ Connected, but no search results";
                                SpotifyTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                            }
                        }
                        else
                        {
                            SpotifyTestStatus.Text = $"✗ Search failed: {searchResponse.StatusCode}";
                            SpotifyTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                        }
                    }
                    else
                    {
                        SpotifyTestStatus.Text = "✗ Invalid token response";
                        SpotifyTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                    }
                }
                else
                {
                    SpotifyTestStatus.Text = $"✗ Authentication failed: {tokenResponse.StatusCode}";
                    SpotifyTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                }
            }
            catch (Exception ex)
            {
                SpotifyTestStatus.Text = $"✗ Error: {ex.Message}";
                SpotifyTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                System.Diagnostics.Debug.WriteLine($"Spotify test error: {ex}");
            }
            finally
            {
                button.Content = originalContent;
                button.IsEnabled = true;
            }
        }
    }
}