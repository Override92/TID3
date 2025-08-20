// Services.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using TagLib;
using TID3.Models;
using TID3.Utils;

namespace TID3.Services
{
    // Compiled regex for track count patterns - optimized for performance
    internal static class RegexPatterns
    {
        // Using Compiled option for better performance than runtime compilation
        internal static readonly Regex TrackCountPattern = new(@"(\d+)\s*tracks?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
    public class MusicBrainzService
    {
        private HttpClient _client;
        private const string BASE_URL = "https://musicbrainz.org/ws/2/";
        private AppSettings _settings;

        public MusicBrainzService()
        {
            _settings = SettingsManager.LoadSettings();
            _client = HttpClientManager.CreateClientWithUserAgent(_settings.GetUserAgent());
        }

        public async Task<List<MusicBrainzRelease>> SearchReleases(string query)
        {
            try
            {
                var url = $"{BASE_URL}release/?query={Uri.EscapeDataString(query)}&fmt=json&limit=10";
                var response = await _client.GetStringAsync(url);
                using var document = JsonDocument.Parse(response);
                var data = document.RootElement;

                var releases = new List<MusicBrainzRelease>();
                if (data.TryGetProperty("releases", out var releasesElement))
                {
                    foreach (var release in releasesElement.EnumerateArray())
                    {
                        var mbRelease = new MusicBrainzRelease
                        {
                            Id = release.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                            Title = release.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                            Artist = GetArtistFromCredit(release),
                            Date = release.TryGetProperty("date", out var date) ? date.GetString() ?? "" : "",
                            Score = release.TryGetProperty("score", out var score) ? score.GetInt32() : 0,
                            TrackCount = GetMusicBrainzTrackCount(release)
                        };
                        releases.Add(mbRelease);
                    }
                }
                return releases;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MusicBrainz search error: {ex.Message}");
                return [];
            }
        }

        public async Task<MusicBrainzRelease?> GetReleaseDetails(string releaseId)
        {
            try
            {
                var url = $"{BASE_URL}release/{releaseId}?inc=recordings&fmt=json";
                var response = await _client.GetStringAsync(url);
                using var document = JsonDocument.Parse(response);
                var data = document.RootElement;

                var release = new MusicBrainzRelease
                {
                    Id = data.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Title = data.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                    Artist = GetArtistFromCredit(data),
                    Date = data.TryGetProperty("date", out var date) ? date.GetString() ?? "" : ""
                };

                if (data.TryGetProperty("media", out var mediaElement))
                {
                    foreach (var medium in mediaElement.EnumerateArray())
                    {
                        if (medium.TryGetProperty("tracks", out var tracksElement))
                        {
                            foreach (var track in tracksElement.EnumerateArray())
                            {
                                release.Tracks.Add(new MusicBrainzTrack
                                {
                                    Title = track.TryGetProperty("title", out var trackTitle) ? trackTitle.GetString() ?? "" : "",
                                    Artist = GetTrackArtist(track, release.Artist),
                                    Position = track.TryGetProperty("position", out var pos) ? pos.GetInt32() : 0,
                                    Length = track.TryGetProperty("length", out var len) ? len.GetInt32() : 0
                                });
                            }
                        }
                    }
                }

                // Update track count based on loaded tracks
                release.TrackCount = release.Tracks.Count;

                return release;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MusicBrainz details error: {ex.Message}");
                return null;
            }
        }

        public async Task<string?> GetCoverArtUrl(string releaseId)
        {
            try
            {
                var url = $"https://coverartarchive.org/release/{releaseId}";
                var response = await _client.GetStringAsync(url);
                using var document = JsonDocument.Parse(response);
                var data = document.RootElement;

                if (data.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
                {
                    foreach (var image in images.EnumerateArray())
                    {
                        // Look for front cover first
                        if (image.TryGetProperty("front", out var front) && front.GetBoolean())
                        {
                            if (image.TryGetProperty("thumbnails", out var thumbnails))
                            {
                                // Prefer small thumbnail for better performance
                                if (thumbnails.TryGetProperty("small", out var small))
                                    return small.GetString();
                                if (thumbnails.TryGetProperty("large", out var large))
                                    return large.GetString();
                            }
                            // Fallback to full image
                            if (image.TryGetProperty("image", out var imageUrl))
                                return imageUrl.GetString();
                        }
                    }
                    
                    // If no front cover found, use first available image
                    var firstImage = images.EnumerateArray().FirstOrDefault();
                    if (firstImage.TryGetProperty("thumbnails", out var firstThumbnails))
                    {
                        if (firstThumbnails.TryGetProperty("small", out var firstSmall))
                            return firstSmall.GetString();
                    }
                }
            }
            catch (HttpRequestException)
            {
                // Cover art not available - this is normal and not an error
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting MusicBrainz cover art: {ex.Message}");
            }
            return null;
        }

        public async Task<BitmapImage?> LoadCoverArtImage(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
                return null;
                
            try
            {
                var response = await _client.SafeGetAsync(imageUrl);
                if (response?.IsSuccessStatusCode == true)
                {
                    var imageData = await response.Content.ReadAsByteArrayAsync();
                    var bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.StreamSource = new MemoryStream(imageData);
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.DecodePixelWidth = 200; // Optimize for display size
                    bitmapImage.EndInit();
                    bitmapImage.Freeze(); // Make it cross-thread accessible
                    return bitmapImage;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading cover art image: {ex.Message}");
            }
            return null;
        }

        public void RefreshSettings()
        {
            _settings = SettingsManager.LoadSettings();
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add("User-Agent", _settings.GetUserAgent());
        }

        private static string GetArtistFromCredit(JsonElement element)
        {
            if (element.TryGetProperty("artist-credit", out var creditElement) && creditElement.ValueKind == JsonValueKind.Array)
            {
                var firstCredit = creditElement.EnumerateArray().FirstOrDefault();
                if (firstCredit.TryGetProperty("name", out var nameElement))
                {
                    return nameElement.GetString() ?? "Unknown Artist";
                }
            }
            return "Unknown Artist";
        }

        private static string GetTrackArtist(JsonElement track, string fallbackArtist)
        {
            if (track.TryGetProperty("recording", out var recording))
            {
                return GetArtistFromCredit(recording);
            }
            return fallbackArtist;
        }

        private static int GetMusicBrainzTrackCount(JsonElement element)
        {
            // Check if track-count is available in the search response
            if (element.TryGetProperty("track-count", out var trackCountElement) && trackCountElement.ValueKind == JsonValueKind.Number)
            {
                return trackCountElement.GetInt32();
            }

            // Alternative: check if media array is available with track info
            if (element.TryGetProperty("media", out var mediaElement) && mediaElement.ValueKind == JsonValueKind.Array)
            {
                int totalTracks = 0;
                foreach (var medium in mediaElement.EnumerateArray())
                {
                    if (medium.TryGetProperty("track-count", out var mediumTrackCount) && mediumTrackCount.ValueKind == JsonValueKind.Number)
                    {
                        totalTracks += mediumTrackCount.GetInt32();
                    }
                    else if (medium.TryGetProperty("tracks", out var tracksElement) && tracksElement.ValueKind == JsonValueKind.Array)
                    {
                        totalTracks += tracksElement.GetArrayLength();
                    }
                }
                return totalTracks;
            }

            return 0; // Track count not available
        }
    }

    public class DiscogsService
    {
        private HttpClient _client;
        private const string BASE_URL = "https://api.discogs.com/";
        private AppSettings _settings;

        public DiscogsService()
        {
            _settings = SettingsManager.LoadSettings();
            _client = HttpClientManager.CreateClientWithUserAgent(_settings.GetUserAgent());
        }

        public async Task<List<DiscogsRelease>> SearchReleases(string query)
        {
            try
            {
                if (!_settings.HasValidDiscogsCredentials())
                {
                    MessageBox.Show("Discogs API credentials are not configured. Please check Settings.", "API Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return [];
                }

                var url = $"{BASE_URL}database/search?q={Uri.EscapeDataString(query)}&type=release&key={_settings.DiscogsApiKey}&secret={_settings.DiscogsSecret}";
                var response = await _client.GetStringAsync(url);
                using var document = JsonDocument.Parse(response);
                var data = document.RootElement;

                var releases = new List<DiscogsRelease>();
                if (data.TryGetProperty("results", out var resultsElement))
                {
                    foreach (var result in resultsElement.EnumerateArray())
                    {
                        var titleString = result.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "";
                        var albumTitle = ExtractAlbumFromDiscogsTitle(titleString);
                        
                        releases.Add(new DiscogsRelease
                        {
                            Id = result.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                            Title = albumTitle,
                            Artist = GetDiscogsArtistString(result),
                            Year = GetDiscogsYearString(result),
                            Genre = GetDiscogsGenreString(result),
                            TrackCount = GetDiscogsTrackCount(result),
                            CoverArtUrl = GetDiscogsCoverArtUrl(result)
                        });
                    }
                }
                return releases;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Discogs search error: {ex.Message}");
                return [];
            }
        }

        public void RefreshSettings()
        {
            _settings = SettingsManager.LoadSettings();
            _client.DefaultRequestHeaders.Clear();
            _client.DefaultRequestHeaders.Add("User-Agent", _settings.GetUserAgent());
        }

        private static string GetDiscogsArtistString(JsonElement element)
        {
            // Try to get artist from basic_information first (common in search results)
            if (element.TryGetProperty("basic_information", out var basicInfo))
            {
                if (basicInfo.TryGetProperty("artists", out var artistsElement) && artistsElement.ValueKind == JsonValueKind.Array)
                {
                    var artists = artistsElement.EnumerateArray()
                        .Where(artist => artist.TryGetProperty("name", out var nameElement) && !string.IsNullOrEmpty(nameElement.GetString()))
                        .Select(artist => artist.GetProperty("name").GetString()!)
                        .ToList();
                    if (artists.Count > 0)
                        return string.Join(", ", artists);
                }
            }

            // Try direct artist field as string
            if (element.TryGetProperty("artist", out var artistElement))
            {
                if (artistElement.ValueKind == JsonValueKind.String)
                {
                    var artistName = artistElement.GetString();
                    if (!string.IsNullOrEmpty(artistName))
                        return artistName;
                }
                else if (artistElement.ValueKind == JsonValueKind.Array)
                {
                    var artists = artistElement.EnumerateArray()
                        .Where(artist => artist.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(artist.GetString()))
                        .Select(artist => artist.GetString()!)
                        .ToList();
                    if (artists.Count > 0)
                        return string.Join(", ", artists);
                }
            }

            // Try to extract artist from title (format: "Artist - Album")
            if (element.TryGetProperty("title", out var titleElement))
            {
                var title = titleElement.GetString();
                if (!string.IsNullOrEmpty(title) && title.Contains(" - "))
                {
                    var parts = title.Split([" - "], 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[0]))
                    {
                        return parts[0].Trim();
                    }
                }
            }

            return "Unknown Artist";
        }

        private static string GetDiscogsGenreString(JsonElement element)
        {
            if (element.TryGetProperty("genre", out var genreElement) && genreElement.ValueKind == JsonValueKind.Array)
            {
                var genres = genreElement.EnumerateArray()
                    .Where(genre => genre.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(genre.GetString()))
                    .Select(genre => genre.GetString()!)
                    .ToList();
                return string.Join(", ", genres);
            }
            return "";
        }

        private static string ExtractAlbumFromDiscogsTitle(string titleString)
        {
            if (string.IsNullOrEmpty(titleString))
                return "";

            // Discogs title format is typically "Artist - Album"
            // We want to extract just the album part
            if (titleString.Contains(" - "))
            {
                var parts = titleString.Split([" - "], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return parts[1].Trim();
                }
            }

            // If no " - " separator found, return the full title as album
            return titleString.Trim();
        }

        private static string GetDiscogsYearString(JsonElement element)
        {
            if (element.TryGetProperty("year", out var yearElement))
            {
                return yearElement.ValueKind switch
                {
                    JsonValueKind.Number => yearElement.GetInt32().ToString(),
                    JsonValueKind.String => yearElement.GetString() ?? "",
                    _ => ""
                };
            }
            return "";
        }

        private static int GetDiscogsTrackCount(JsonElement element)
        {
            // Check basic_information for tracklist first (common in search results)
            if (element.TryGetProperty("basic_information", out var basicInfo))
            {
                if (basicInfo.TryGetProperty("tracklist", out var tracklistElement) && tracklistElement.ValueKind == JsonValueKind.Array)
                {
                    return tracklistElement.GetArrayLength();
                }
                
                // Check for formats in basic_information
                if (basicInfo.TryGetProperty("formats", out var formatsElement) && formatsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var format in formatsElement.EnumerateArray())
                    {
                        if (format.TryGetProperty("descriptions", out var descriptionsElement) && descriptionsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var desc in descriptionsElement.EnumerateArray())
                            {
                                var descStr = desc.GetString() ?? "";
                                var trackMatch = RegexPatterns.TrackCountPattern.Match(descStr);
                                if (trackMatch.Success && int.TryParse(trackMatch.Groups[1].Value, out int trackCount))
                                {
                                    return trackCount;
                                }
                            }
                        }
                    }
                }
            }

            // Check if tracklist is available in search results
            if (element.TryGetProperty("tracklist", out var tracklistElement2) && tracklistElement2.ValueKind == JsonValueKind.Array)
            {
                return tracklistElement2.GetArrayLength();
            }

            // Check if format information contains track count
            if (element.TryGetProperty("format", out var formatElement) && formatElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var format in formatElement.EnumerateArray())
                {
                    if (format.ValueKind == JsonValueKind.String)
                    {
                        var formatStr = format.GetString() ?? "";
                        // Look for track count patterns like "CD, Album, 12 tracks"
                        var trackMatch = RegexPatterns.TrackCountPattern.Match(formatStr);
                        if (trackMatch.Success && int.TryParse(trackMatch.Groups[1].Value, out int trackCount))
                        {
                            return trackCount;
                        }
                    }
                }
            }

            // Try to extract from title if it contains track count info
            if (element.TryGetProperty("title", out var titleElement))
            {
                var title = titleElement.GetString() ?? "";
                var trackMatch = RegexPatterns.TrackCountPattern.Match(title);
                if (trackMatch.Success && int.TryParse(trackMatch.Groups[1].Value, out int trackCount))
                {
                    return trackCount;
                }
            }

            return 0; // Track count not available
        }
        
        private static string? GetDiscogsCoverArtUrl(JsonElement element)
        {
            if (element.TryGetProperty("cover_image", out var coverElement))
            {
                var url = coverElement.GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    return url;
                }
            }
            
            // Fallback to thumb if cover_image is not available
            if (element.TryGetProperty("thumb", out var thumbElement))
            {
                return thumbElement.GetString();
            }
            
            return null;
        }
    }

    public class TagService
    {
        public AudioFileInfo? LoadFile(string filePath)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);
                
                // Cache tag and properties references to avoid repeated property access
                var tag = file.Tag;
                var properties = file.Properties;
                
                var audioFile = new AudioFileInfo
                {
                    FilePath = filePath,
                    Title = tag.Title ?? string.Empty,
                    Artist = JoinStringArray(tag.Performers),
                    Album = tag.Album ?? string.Empty,
                    Genre = JoinStringArray(tag.Genres),
                    Year = tag.Year,
                    Track = tag.Track,
                    AlbumArtist = JoinStringArray(tag.AlbumArtists),
                    Comment = tag.Comment ?? string.Empty,
                    Duration = FormatDuration(properties.Duration),
                    Bitrate = FormatBitrate(properties.AudioBitrate),
                    FileSize = FormatFileSize(new FileInfo(filePath).Length),
                    LocalCover = LoadAlbumCover(tag) ?? LoadCoverArtFromFolder(filePath),
                    CoverArtSource = GetCoverArtSource(tag, filePath)
                };
                return audioFile;
            }
            catch (Exception ex)
            {
                // Log error but don't show MessageBox in parallel context
                System.Diagnostics.Debug.WriteLine($"Error loading file {filePath}: {ex.Message}");
                return null;
            }
        }
        
        // Optimized string joining to avoid unnecessary allocations
        private static string JoinStringArray(string[]? array)
        {
            if (array == null || array.Length == 0)
                return string.Empty;
            
            if (array.Length == 1)
                return array[0] ?? string.Empty;
            
            return string.Join(", ", array);
        }
        
        // Optimized duration formatting without string interpolation overhead
        private static string FormatDuration(TimeSpan duration)
        {
            return duration.ToString(@"mm\:ss");
        }
        
        // Optimized bitrate formatting to reduce allocations
        private static string FormatBitrate(int bitrate)
        {
            return bitrate > 0 ? $"{bitrate} kbps" : "Unknown";
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = ["B", "KB", "MB", "GB"];
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        public (bool Success, bool CoverArtSaved) SaveFile(AudioFileInfo audioFile)
        {
            try
            {
                using var file = TagLib.File.Create(audioFile.FilePath);
                file.Tag.Title = audioFile.Title;
                file.Tag.Performers = [.. audioFile.Artist.Split(',').Select(s => s.Trim())];
                file.Tag.Album = audioFile.Album;
                file.Tag.Genres = [.. audioFile.Genre.Split(',').Select(s => s.Trim())];
                file.Tag.Year = audioFile.Year;
                file.Tag.Track = audioFile.Track;
                file.Tag.AlbumArtists = [.. audioFile.AlbumArtist.Split(',').Select(s => s.Trim())];
                file.Tag.Comment = audioFile.Comment;
                file.Save();
                
                // Save cover art to album folder if it's from an online source
                bool coverArtSaved = SaveCoverArtToFolder(audioFile);
                if (coverArtSaved)
                {
                    System.Diagnostics.Debug.WriteLine($"Cover art saved to album folder for: {audioFile.FileName}");
                }
                
                return (true, coverArtSaved);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file {audioFile.FilePath}: {ex.Message}");
                return (false, false);
            }
        }

        public (int SavedFiles, int CoverArtsSaved) BatchUpdate(IEnumerable<AudioFileInfo> files, AudioFileInfo template)
        {
            int savedFiles = 0;
            int coverArtsSaved = 0;
            
            foreach (var file in files)
            {
                if (!string.IsNullOrEmpty(template.Album)) file.Album = template.Album;
                if (!string.IsNullOrEmpty(template.AlbumArtist)) file.AlbumArtist = template.AlbumArtist;
                if (!string.IsNullOrEmpty(template.Genre)) file.Genre = template.Genre;
                if (template.Year > 0) file.Year = template.Year;
                
                var (success, coverArtSaved) = SaveFile(file);
                if (success) savedFiles++;
                if (coverArtSaved) coverArtsSaved++;
            }
            
            return (savedFiles, coverArtsSaved);
        }

        private BitmapImage? LoadAlbumCover(Tag tag)
        {
            try
            {
                if (tag.Pictures != null && tag.Pictures.Length > 0)
                {
                    var picture = tag.Pictures[0]; // Get the first image
                    if (picture.Data?.Data != null && picture.Data.Count > 0)
                    {
                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = new MemoryStream(picture.Data.Data);
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.DecodePixelWidth = 200; // Optimize for display size
                        bitmapImage.EndInit();
                        bitmapImage.Freeze(); // Make it cross-thread accessible
                        return bitmapImage;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading album cover: {ex.Message}");
            }
            return null;
        }


        private string GetCoverArtSource(Tag tag, string filePath)
        {
            try
            {
                // Check for embedded cover art first
                if (tag.Pictures != null && tag.Pictures.Length > 0)
                {
                    return "Embedded in file";
                }

                // Check for folder-based cover art
                var folderPath = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    string[] coverArtNames = {
                        "folder.jpg", "folder.jpeg", "folder.png",
                        "cover.jpg", "cover.jpeg", "cover.png",
                        "front.jpg", "front.jpeg", "front.png",
                        "albumart.jpg", "albumart.jpeg", "albumart.png",
                        "album.jpg", "album.jpeg", "album.png"
                    };

                    foreach (var fileName in coverArtNames)
                    {
                        var coverPath = Path.Combine(folderPath, fileName);
                        if (System.IO.File.Exists(coverPath))
                        {
                            return $"Local file: {fileName}";
                        }
                    }

                    // Check for any image files
                    var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                    var imageFiles = Directory.GetFiles(folderPath)
                        .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLower()))
                        .ToArray();

                    if (imageFiles.Length > 0)
                    {
                        var firstImage = Path.GetFileName(imageFiles.OrderBy(f => f).First());
                        return $"Folder file: {firstImage}";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking cover art source: {ex.Message}");
            }
            return "";
        }

        public BitmapImage? LoadCoverArtFromFolder(string audioFilePath)
        {
            try
            {
                var folderPath = Path.GetDirectoryName(audioFilePath);
                if (string.IsNullOrEmpty(folderPath))
                    return null;

                // Common cover art filenames in order of preference
                string[] coverArtNames = {
                    "folder.jpg", "folder.jpeg", "folder.png",
                    "cover.jpg", "cover.jpeg", "cover.png",
                    "front.jpg", "front.jpeg", "front.png",
                    "albumart.jpg", "albumart.jpeg", "albumart.png",
                    "album.jpg", "album.jpeg", "album.png",
                    // Also check for album-specific names
                };

                // First, try standard names
                foreach (var fileName in coverArtNames)
                {
                    var coverPath = Path.Combine(folderPath, fileName);
                    if (System.IO.File.Exists(coverPath))
                    {
                        return LoadImageFromFile(coverPath);
                    }
                }

                // If no standard names found, look for any image files
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                var imageFiles = Directory.GetFiles(folderPath)
                    .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLower()))
                    .OrderBy(file => Path.GetFileName(file).ToLower()) // Alphabetical order
                    .ToArray();

                // Try to find images with album-related keywords
                var albumKeywords = new[] { "cover", "front", "album", "folder" };
                foreach (var imageFile in imageFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(imageFile).ToLower();
                    if (albumKeywords.Any(keyword => fileName.Contains(keyword)))
                    {
                        return LoadImageFromFile(imageFile);
                    }
                }

                // If still nothing found, use the first image file (if any)
                if (imageFiles.Length > 0)
                {
                    return LoadImageFromFile(imageFiles[0]);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading cover art from folder: {ex.Message}");
            }

            return null;
        }

        private BitmapImage? LoadImageFromFile(string imagePath)
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(imagePath);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.DecodePixelWidth = 200; // Optimize for display size
                bitmapImage.EndInit();
                bitmapImage.Freeze(); // Make it cross-thread accessible
                return bitmapImage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image from file {imagePath}: {ex.Message}");
                return null;
            }
        }

        public void RefreshFolderCoverArt(IEnumerable<AudioFileInfo> audioFiles)
        {
            // Group files by folder to optimize cover art loading
            var folderGroups = audioFiles
                .Where(f => !string.IsNullOrEmpty(f.FilePath))
                .GroupBy(f => Path.GetDirectoryName(f.FilePath))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToList();

            foreach (var folderGroup in folderGroups)
            {
                var folderPath = folderGroup.Key!;
                
                // Load cover art once per folder for efficiency
                var folderCoverArt = LoadCoverArtFromFolder(Path.Combine(folderPath, "dummy.mp3"));
                
                if (folderCoverArt != null)
                {
                    // Determine the cover art source for this folder
                    string folderCoverSource = GetFolderCoverArtSource(folderPath);
                    
                    foreach (var file in folderGroup)
                    {
                        // Only update if file doesn't already have embedded cover art
                        if (file.AlbumCover == null || !file.CoverArtSource.StartsWith("Embedded"))
                        {
                            file.AlbumCover = folderCoverArt;
                            file.CoverArtSource = folderCoverSource;
                        }
                    }
                }
            }
        }

        private string GetFolderCoverArtSource(string folderPath)
        {
            try
            {
                string[] coverArtNames = {
                    "folder.jpg", "folder.jpeg", "folder.png",
                    "cover.jpg", "cover.jpeg", "cover.png",
                    "front.jpg", "front.jpeg", "front.png",
                    "albumart.jpg", "albumart.jpeg", "albumart.png",
                    "album.jpg", "album.jpeg", "album.png"
                };

                foreach (var fileName in coverArtNames)
                {
                    var coverPath = Path.Combine(folderPath, fileName);
                    if (System.IO.File.Exists(coverPath))
                    {
                        return $"Folder file: {fileName}";
                    }
                }

                // Check for any image files
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                var imageFiles = Directory.GetFiles(folderPath)
                    .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLower()))
                    .ToArray();

                if (imageFiles.Length > 0)
                {
                    var firstImage = Path.GetFileName(imageFiles.OrderBy(f => f).First());
                    return $"Folder file: {firstImage}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting folder cover art source: {ex.Message}");
            }

            return "";
        }

        public bool SaveCoverArtToFolder(AudioFileInfo audioFile)
        {
            try
            {
                // Only save cover art if it's from an online source (not embedded or already from folder)
                if (audioFile.AlbumCover == null || 
                    audioFile.CoverArtSource == "Embedded in file" || 
                    audioFile.CoverArtSource.StartsWith("Folder file:") ||
                    string.IsNullOrEmpty(audioFile.CoverArtSource))
                    return false;

                var folderPath = Path.GetDirectoryName(audioFile.FilePath);
                if (string.IsNullOrEmpty(folderPath))
                    return false;

                // Create filename for cover art
                string coverFileName = GetCoverArtFileName(audioFile);
                string coverFilePath = Path.Combine(folderPath, coverFileName);

                // Don't overwrite if file already exists
                if (System.IO.File.Exists(coverFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Cover art file already exists: {coverFilePath}");
                    return false;
                }

                // Convert BitmapImage to byte array and save
                byte[]? imageBytes = BitmapImageToByteArray(audioFile.AlbumCover);
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    System.IO.File.WriteAllBytes(coverFilePath, imageBytes);
                    System.Diagnostics.Debug.WriteLine($"Saved cover art to: {coverFilePath} (Source: {audioFile.CoverArtSource})");
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving cover art to folder: {ex.Message}");
                return false;
            }
        }

        private string GetCoverArtFileName(AudioFileInfo audioFile)
        {
            // Check for existing cover art files with common names first
            var folderPath = Path.GetDirectoryName(audioFile.FilePath);
            if (!string.IsNullOrEmpty(folderPath))
            {
                string[] commonNames = { "folder.jpg", "cover.jpg", "front.jpg", "albumart.jpg" };
                
                foreach (var name in commonNames)
                {
                    if (!System.IO.File.Exists(Path.Combine(folderPath, name)))
                    {
                        return name; // Use the first available common name
                    }
                }
            }

            // If all common names exist, try album-specific name
            if (!string.IsNullOrEmpty(audioFile.Album))
            {
                string safeName = SanitizeFileName(audioFile.Album);
                return $"{safeName}.jpg";
            }
            
            // Last resort: use timestamp to avoid conflicts
            return $"cover_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "cover";

            // Remove invalid filename characters
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
            
            // Replace spaces with underscores and limit length
            sanitized = sanitized.Replace(' ', '_');
            if (sanitized.Length > 50)
                sanitized = sanitized[..50];
                
            return string.IsNullOrEmpty(sanitized) ? "cover" : sanitized;
        }

        private byte[]? BitmapImageToByteArray(System.Windows.Media.Imaging.BitmapImage? bitmapImage)
        {
            if (bitmapImage == null)
                return null;

            try
            {
                // Try to get the original source stream if available
                if (bitmapImage.StreamSource != null && bitmapImage.StreamSource.CanRead)
                {
                    bitmapImage.StreamSource.Position = 0;
                    using var memoryStream = new MemoryStream();
                    bitmapImage.StreamSource.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }

                // Fallback: encode as JPEG
                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapImage));
                
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting bitmap to byte array: {ex.Message}");
                return null;
            }
        }
    }
}