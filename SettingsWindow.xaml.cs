using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace TID3
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private AppSettings _currentSettings = null!;

        public SettingsWindow()
        {
            InitializeComponent();
            LoadVersion();
            LoadSettings();
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
                        var buildDate = DateTime.MinValue.AddDays(version.Build);
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

            // File Processing
            _currentSettings.AutoSave = AutoSaveCheckBox.IsChecked ?? false;
            _currentSettings.CreateBackup = CreateBackupCheckBox.IsChecked ?? false;
            _currentSettings.IncludeSubdirectories = IncludeSubdirectoriesCheckBox.IsChecked ?? true;
            _currentSettings.SupportedExtensions = SupportedExtensionsTextBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.Trim().ToLowerInvariant())
                .ToArray();
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

                using (var client = HttpClientManager.CreateClientWithUserAgent("TID3/1.0"))
                {
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
            }
            catch (Exception ex)
            {
                DiscogsTestStatus.Text = $"✗ Error: {ex.Message}";
                DiscogsTestStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void BrowseCacheDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                folderDialog.Description = "Select cache directory";
                folderDialog.SelectedPath = CacheDirectoryTextBox.Text;

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    CacheDirectoryTextBox.Text = folderDialog.SelectedPath;
                }
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
                Process.Start(new ProcessStartInfo
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
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
    }
}