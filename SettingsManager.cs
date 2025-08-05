using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TID3
{
    public enum AppTheme
    {
        Dark = 0,
        Light = 1,
        Auto = 2
    }

    public enum AppLanguage
    {
        English = 0,
        German = 1,
        French = 2,
        Spanish = 3
    }

    public class AppSettings
    {
        // API Configuration
        [JsonPropertyName("musicbrainz_user_agent")]
        public string MusicBrainzUserAgent { get; set; } = "TID3/1.0 (your-email@example.com)";

        [JsonPropertyName("discogs_api_key")]
        public string DiscogsApiKey { get; set; } = "";

        [JsonPropertyName("discogs_secret")]
        public string DiscogsSecret { get; set; } = "";

        // File Processing
        [JsonPropertyName("auto_save")]
        public bool AutoSave { get; set; } = false;

        [JsonPropertyName("create_backup")]
        public bool CreateBackup { get; set; } = false;

        [JsonPropertyName("include_subdirectories")]
        public bool IncludeSubdirectories { get; set; } = true;

        [JsonPropertyName("supported_extensions")]
        public string[] SupportedExtensions { get; set; } = { ".mp3", ".flac", ".m4a", ".aac", ".wav", ".wma" };

        [JsonPropertyName("max_concurrent_operations")]
        public int MaxConcurrentOperations { get; set; } = 4;

        // User Interface
        [JsonPropertyName("theme")]
        public AppTheme Theme { get; set; } = AppTheme.Dark;

        [JsonPropertyName("language")]
        public AppLanguage Language { get; set; } = AppLanguage.English;

        [JsonPropertyName("show_file_extensions")]
        public bool ShowFileExtensions { get; set; } = true;

        [JsonPropertyName("show_tooltips")]
        public bool ShowToolTips { get; set; } = true;

        [JsonPropertyName("minimize_to_tray")]
        public bool MinimizeToTray { get; set; } = false;

        [JsonPropertyName("font_size")]
        public int FontSize { get; set; } = 12;

        // Advanced Settings
        [JsonPropertyName("enable_logging")]
        public bool EnableLogging { get; set; } = false;

        [JsonPropertyName("check_for_updates")]
        public bool CheckForUpdates { get; set; } = true;

        [JsonPropertyName("send_usage_stats")]
        public bool SendUsageStats { get; set; } = false;

        [JsonPropertyName("cache_directory")]
        public string CacheDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TID3", "Cache");

        // Window Settings
        [JsonPropertyName("window_width")]
        public double WindowWidth { get; set; } = 1200;

        [JsonPropertyName("window_height")]
        public double WindowHeight { get; set; } = 800;

        [JsonPropertyName("window_maximized")]
        public bool WindowMaximized { get; set; } = false;

        [JsonPropertyName("last_directory")]
        public string LastDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        // Application Settings
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("first_run")]
        public bool FirstRun { get; set; } = true;

        [JsonPropertyName("last_update_check")]
        public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;

        // Performance Settings
        [JsonPropertyName("enable_threading")]
        public bool EnableThreading { get; set; } = true;

        [JsonPropertyName("cache_search_results")]
        public bool CacheSearchResults { get; set; } = true;

        [JsonPropertyName("search_result_cache_hours")]
        public int SearchResultCacheHours { get; set; } = 24;

        // Default Tag Settings
        [JsonPropertyName("default_genre")]
        public string DefaultGenre { get; set; } = "";

        [JsonPropertyName("default_album_artist")]
        public string DefaultAlbumArtist { get; set; } = "";

        [JsonPropertyName("auto_capitalize_tags")]
        public bool AutoCapitalizeTags { get; set; } = true;

        [JsonPropertyName("remove_feat_from_title")]
        public bool RemoveFeatFromTitle { get; set; } = false;

        [JsonPropertyName("standardize_separators")]
        public bool StandardizeSeparators { get; set; } = true;
    }

    public class SettingsManager
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TID3");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

        public AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    // Create default settings file
                    var defaultSettings = new AppSettings();
                    SaveSettings(defaultSettings);
                    return defaultSettings;
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);

                // Validate and fix settings if necessary
                if (settings != null)
                    ValidateSettings(settings);

                return settings ?? new AppSettings();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error loading settings: {ex.Message}\nUsing default settings.",
                    "Settings Load Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);

                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(SettingsDirectory))
                {
                    Directory.CreateDirectory(SettingsDirectory);
                }

                // Validate settings before saving
                ValidateSettings(settings);

                // Serialize settings to JSON
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFilePath, json);

                // Ensure cache directory exists
                if (!Directory.Exists(settings.CacheDirectory))
                {
                    Directory.CreateDirectory(settings.CacheDirectory);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error saving settings: {ex.Message}",
                    "Settings Save Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void ValidateSettings(AppSettings? settings)
        {
            if (settings == null) return;
            
            // Validate numeric ranges
            if (settings.MaxConcurrentOperations < 1 || settings.MaxConcurrentOperations > 20)
                settings.MaxConcurrentOperations = 4;

            if (settings.FontSize < 8 || settings.FontSize > 18)
                settings.FontSize = 12;

            if (settings.SearchResultCacheHours < 1 || settings.SearchResultCacheHours > 168) // Max 1 week
                settings.SearchResultCacheHours = 24;

            // Validate file extensions
            if (settings.SupportedExtensions == null || settings.SupportedExtensions.Length == 0)
            {
                settings.SupportedExtensions = new[] { ".mp3", ".flac", ".m4a", ".aac", ".wav", ".wma" };
            }
            else
            {
                // Ensure all extensions start with a dot and are lowercase
                settings.SupportedExtensions = settings.SupportedExtensions
                    .Where(ext => !string.IsNullOrWhiteSpace(ext))
                    .Select(ext => ext.Trim().ToLowerInvariant())
                    .Select(ext => ext.StartsWith(".") ? ext : "." + ext)
                    .Distinct()
                    .ToArray();
            }

            // Validate directories
            if (string.IsNullOrWhiteSpace(settings.CacheDirectory))
            {
                settings.CacheDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TID3", "Cache");
            }

            if (string.IsNullOrWhiteSpace(settings.LastDirectory) || !Directory.Exists(settings.LastDirectory))
            {
                settings.LastDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            }

            // Validate window dimensions
            if (settings.WindowWidth < 800) settings.WindowWidth = 1200;
            if (settings.WindowHeight < 600) settings.WindowHeight = 800;

            // Validate API settings
            if (string.IsNullOrWhiteSpace(settings.MusicBrainzUserAgent))
            {
                settings.MusicBrainzUserAgent = "TID3/1.0 (your-email@example.com)";
            }

            // Update version if necessary
            settings.Version = "1.0.0";
        }

        public void ResetToDefaults()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
                }

                var defaultSettings = new AppSettings();
                SaveSettings(defaultSettings);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error resetting settings: {ex.Message}",
                    "Settings Reset Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public void BackupSettings()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return;

                var backupPath = Path.Combine(SettingsDirectory, $"settings_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.Copy(SettingsFilePath, backupPath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error creating settings backup: {ex.Message}",
                    "Backup Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        public static string GetSettingsDirectory() => SettingsDirectory;
        public static string GetSettingsFilePath() => SettingsFilePath;
    }

    // Extension methods for easier settings access
    public static class SettingsExtensions
    {
        public static bool IsExtensionSupported(this AppSettings settings, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return settings.SupportedExtensions.Contains(extension);
        }

        public static string GetUserAgent(this AppSettings settings)
        {
            return string.IsNullOrWhiteSpace(settings.MusicBrainzUserAgent)
                ? "TID3/1.0"
                : settings.MusicBrainzUserAgent;
        }

        public static bool HasValidDiscogsCredentials(this AppSettings settings)
        {
            return !string.IsNullOrWhiteSpace(settings.DiscogsApiKey);
        }
    }
}