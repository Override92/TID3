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
        private const string BASE_URL = "https://musicbrainz.org/ws/2/";
        private AppSettings _settings;

        public MusicBrainzService()
        {
            _settings = SettingsManager.LoadSettings();
        }

        public async Task<List<MusicBrainzRelease>> SearchReleases(string query)
        {
            try
            {
                var url = $"{BASE_URL}release/?query={Uri.EscapeDataString(query)}&fmt=json&limit=10";
                var response = await HttpClientManager.MusicBrainz.GetStringAsync(url);
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
                var response = await HttpClientManager.MusicBrainz.GetStringAsync(url);
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
                var response = await HttpClientManager.MusicBrainz.GetStringAsync(url);
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
                TID3Logger.Warning("Images", "Error getting MusicBrainz cover art", ex, component: "TagService");
            }
            return null;
        }

        public async Task<BitmapImage?> LoadCoverArtImage(string? imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
                return null;
                
            try
            {
                return await ImageHelper.CreateBitmapFromUrlAsync(imageUrl, HttpClientManager.MusicBrainz);
            }
            catch (Exception ex)
            {
                TID3Logger.Warning("Images", "Error loading cover art image", ex, component: "TagService");
            }
            return null;
        }

        public void RefreshSettings()
        {
            _settings = SettingsManager.LoadSettings();
            // Headers are now managed by HttpClientManager.MusicBrainz
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
        private const string BASE_URL = "https://api.discogs.com/";
        private AppSettings _settings;

        public DiscogsService()
        {
            _settings = SettingsManager.LoadSettings();
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
                var response = await HttpClientManager.MusicBrainz.GetStringAsync(url);
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
            // Headers are now managed by HttpClientManager.MusicBrainz
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
            using var scope = TID3Logger.BeginScope("Files", "LoadFile", new { FileName = Path.GetFileName(filePath) }, "TagService");
            TID3Logger.Images.LogFileInfo(filePath, "TagService");
            try
            {
                using var file = TagLib.File.Create(filePath);
                TID3Logger.Debug("Tags", "TagLib file created successfully", component: "TagService");
                
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
                TID3Logger.Debug("Tags", "Returning AudioFileInfo", new { 
                    HasLocalCover = audioFile.LocalCover != null,
                    Title = audioFile.Title,
                    Artist = audioFile.Artist,
                    Album = audioFile.Album
                }, "TagService");
                if (audioFile.LocalCover != null)
                {
                    TID3Logger.Images.LogImageDetails(audioFile.LocalCover, "Final LocalCover", "TagService");
                }
                return audioFile;
            }
            catch (Exception ex)
            {
                // Log error but don't show MessageBox in parallel context
                TID3Logger.Error("Files", "Error loading file", ex, new { FilePath = filePath }, "TagService");
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

        public (bool Success, bool CoverArtSaved) SaveFile(AudioFileInfo audioFile, bool replaceExistingCoverArt = false)
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
                bool coverArtSaved = SaveCoverArtToFolder(audioFile, replaceExistingCoverArt);
                if (coverArtSaved)
                {
                    TID3Logger.Info("Images", "Cover art saved to album folder", new { FileName = audioFile.FileName }, "TagService");
                }
                
                return (true, coverArtSaved);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file {audioFile.FilePath}: {ex.Message}");
                return (false, false);
            }
        }

        public (int SavedFiles, int CoverArtsSaved) BatchUpdate(IEnumerable<AudioFileInfo> files, AudioFileInfo template, bool replaceExistingCoverArt = false)
        {
            int savedFiles = 0;
            int coverArtsSaved = 0;
            
            foreach (var file in files)
            {
                if (!string.IsNullOrEmpty(template.Album)) file.Album = template.Album;
                if (!string.IsNullOrEmpty(template.AlbumArtist)) file.AlbumArtist = template.AlbumArtist;
                if (!string.IsNullOrEmpty(template.Genre)) file.Genre = template.Genre;
                if (template.Year > 0) file.Year = template.Year;
                
                var (success, coverArtSaved) = SaveFile(file, replaceExistingCoverArt);
                if (success) savedFiles++;
                if (coverArtSaved) coverArtsSaved++;
            }
            
            return (savedFiles, coverArtsSaved);
        }

        private BitmapImage? LoadAlbumCover(Tag tag)
        {
            using var scope = TID3Logger.BeginScope("Images", "LoadAlbumCover", component: "TagService");
            try
            {
                if (tag == null)
                {
                    TID3Logger.Debug("Images", "Tag is NULL - cannot load embedded cover", component: "TagService");
                    return null;
                }

                TID3Logger.Debug("Images", "Tag pictures info", new { 
                    HasPictures = tag.Pictures != null,
                    PictureCount = tag.Pictures?.Length ?? 0
                }, "TagService");
                
                if (tag.Pictures != null && tag.Pictures.Length > 0)
                {
                    var picture = tag.Pictures[0]; // Get the first image
                    TID3Logger.Debug("Images", "Processing first embedded picture", new {
                        Type = picture?.Type.ToString(),
                        MimeType = picture?.MimeType,
                        DataSizeBytes = picture?.Data?.Count ?? 0,
                        Description = picture?.Description
                    }, "TagService");
                    
                    var result = ImageHelper.CreateBitmapFromTagPicture(picture);
                    if (result != null)
                    {
                        TID3Logger.Images.LogImageDetails(result, "LoadAlbumCover result", "TagService");
                    }
                    else
                    {
                        TID3Logger.Warning("Images", "Failed to create bitmap from embedded picture", component: "TagService");
                    }
                    return result;
                }
                else
                {
                    TID3Logger.Debug("Images", "No pictures found in tag - will try folder fallback", component: "TagService");
                }
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "LoadAlbumCover failed", ex, component: "TagService");
            }
            TID3Logger.Debug("Images", "LoadAlbumCover returning null", component: "TagService");
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
                        var coverPath = SecureCombinePath(folderPath, fileName);
                        if (coverPath != null && System.IO.File.Exists(coverPath))
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
                TID3Logger.Warning("Images", "Error checking cover art source", ex, component: "TagService");
            }
            return "";
        }

        public BitmapImage? LoadCoverArtFromFolder(string audioFilePath)
        {
            using var scope = TID3Logger.BeginScope("Images", "LoadCoverArtFromFolder", new { AudioFile = Path.GetFileName(audioFilePath) }, "TagService");
            try
            {
                var folderPath = Path.GetDirectoryName(audioFilePath);
                TID3Logger.Debug("Images", "Checking folder for cover art", new { FolderPath = folderPath }, "TagService");
                if (string.IsNullOrEmpty(folderPath))
                {
                    TID3Logger.Debug("Images", "FolderPath is null/empty - cannot search for folder covers", component: "TagService");
                    return null;
                }

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
                TID3Logger.Debug("Images", "Trying standard cover filenames", new { FileNames = coverArtNames }, "TagService");
                foreach (var fileName in coverArtNames)
                {
                    var coverPath = SecureCombinePath(folderPath, fileName);
                    if (coverPath != null && System.IO.File.Exists(coverPath))
                    {
                        TID3Logger.Debug("Images", "Found standard cover file", new { FileName = fileName, FilePath = coverPath }, "TagService");
                        TID3Logger.Images.LogFileInfo(coverPath, "TagService");
                        
                        var result = LoadImageFromFile(coverPath);
                        
                        if (result != null)
                        {
                            TID3Logger.Images.LogImageDetails(result, "Folder cover result", "TagService");
                            return result;
                        }
                        else
                        {
                            TID3Logger.Warning("Images", "Failed to load standard cover file", new { FilePath = coverPath }, "TagService");
                        }
                    }
                }
                TID3Logger.Debug("Images", "No standard cover files found, searching for any image files", component: "TagService");

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
                TID3Logger.Warning("Images", "Error loading cover art from folder", ex, component: "TagService");
            }

            return null;
        }

        private BitmapImage? LoadImageFromFile(string imagePath)
        {
            try
            {
                // Create the display-optimized image (dimensions are handled by ImageHelper)
                var bitmapImage = ImageHelper.CreateBitmapFromFile(imagePath);
                if (bitmapImage == null)
                    return null;
                
                // Get the stored dimensions for logging
                var (originalWidth, originalHeight) = ImageHelper.GetOriginalDimensions(bitmapImage);
                
                TID3Logger.Debug("Images", "Image loaded from file", new { FilePath = imagePath, OriginalWidth = originalWidth, OriginalHeight = originalHeight, ResizedWidth = bitmapImage.PixelWidth, ResizedHeight = bitmapImage.PixelHeight }, "TagService");
                
                return bitmapImage;
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Error loading image from file", ex, new { FilePath = imagePath }, "TagService");
                return null;
            }
        }

        // Dependency properties to store original dimensions
        public static readonly DependencyProperty OriginalWidthProperty = 
            DependencyProperty.RegisterAttached("OriginalWidth", typeof(int), typeof(TagService));
        public static readonly DependencyProperty OriginalHeightProperty = 
            DependencyProperty.RegisterAttached("OriginalHeight", typeof(int), typeof(TagService));

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
                    var coverPath = SecureCombinePath(folderPath, fileName);
                    if (coverPath != null && System.IO.File.Exists(coverPath))
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
                TID3Logger.Warning("Images", "Error getting folder cover art source", ex, component: "TagService");
            }

            return "";
        }

        public bool SaveCoverArtToFolder(AudioFileInfo audioFile, bool replaceExisting = false)
        {
            using var scope = TID3Logger.BeginScope("Images", "SaveCoverArtToFolder", new {
                FileName = audioFile.FileName,
                CoverSource = audioFile.CoverArtSource,
                HasCover = audioFile.AlbumCover != null,
                ReplaceExisting = replaceExisting
            }, "TagService");

            try
            {
                // Only save cover art if it's from an online source (not embedded or already from folder)
                if (audioFile.AlbumCover == null || 
                    audioFile.CoverArtSource == "Embedded in file" || 
                    audioFile.CoverArtSource.StartsWith("Folder file:") ||
                    string.IsNullOrEmpty(audioFile.CoverArtSource))
                {
                    TID3Logger.Debug("Images", "Skipping cover art save - not an online source", new {
                        CoverSource = audioFile.CoverArtSource,
                        HasCover = audioFile.AlbumCover != null
                    }, "TagService");
                    return false;
                }

                var folderPath = Path.GetDirectoryName(audioFile.FilePath);
                if (string.IsNullOrEmpty(folderPath))
                {
                    TID3Logger.Warning("Images", "Cannot determine folder path for cover art save", component: "TagService");
                    return false;
                }

                // Create filename for cover art
                string coverFileName = GetCoverArtFileName(audioFile);
                string? coverFilePath = SecureCombinePath(folderPath, coverFileName);
                
                if (coverFilePath == null)
                {
                    TID3Logger.Error("Images", "Invalid path combination for cover art", null, new {
                        FolderPath = folderPath,
                        CoverFileName = coverFileName
                    }, "TagService");
                    return false;
                }

                TID3Logger.Debug("Images", "Cover art save path determined", new {
                    CoverFileName = coverFileName,
                    TargetFilePath = coverFilePath
                }, "TagService");
                
                // Check if file exists and whether we should replace it
                if (System.IO.File.Exists(coverFilePath))
                {
                    if (!replaceExisting)
                    {
                        TID3Logger.Debug("Images", "Cover art file already exists and replace=false", new {
                            FilePath = coverFilePath
                        }, "TagService");
                        return false;
                    }
                    else
                    {
                        TID3Logger.Debug("Images", "Replacing existing cover art file", new {
                            FilePath = coverFilePath
                        }, "TagService");
                    }
                }
                else
                {
                    TID3Logger.Debug("Images", "Creating new cover art file", new {
                        FilePath = coverFilePath
                    }, "TagService");
                }

                // Convert BitmapImage to byte array and save
                byte[]? imageBytes = BitmapImageToByteArray(audioFile.AlbumCover);
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    System.IO.File.WriteAllBytes(coverFilePath, imageBytes);
                    TID3Logger.Info("Images", "Successfully saved cover art to folder", new {
                        FilePath = coverFilePath,
                        ByteCount = imageBytes.Length,
                        CoverSource = audioFile.CoverArtSource
                    }, "TagService");
                    return true;
                }
                
                TID3Logger.Warning("Images", "Failed to get image bytes for cover art save", component: "TagService");
                return false;
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Error saving cover art to folder", ex, new {
                    FilePath = audioFile.FilePath,
                    CoverSource = audioFile.CoverArtSource
                }, "TagService");
                return false;
            }
        }

        private string GetCoverArtFileName(AudioFileInfo audioFile)
        {
            var folderPath = Path.GetDirectoryName(audioFile.FilePath);
            if (!string.IsNullOrEmpty(folderPath))
            {
                string[] commonNames = { "folder.jpg", "cover.jpg", "front.jpg", "albumart.jpg", "album.jpg", "folder.jpeg", "cover.jpeg", "front.jpeg", "albumart.jpeg", "album.jpeg", "folder.png", "cover.png", "front.png", "albumart.png", "album.png" };
                
                // First, check if there's already a local cover art file that we should replace
                foreach (var name in commonNames)
                {
                    var existingPath = SecureCombinePath(folderPath, name);
                    if (existingPath != null && System.IO.File.Exists(existingPath))
                    {
                        return name; // Use the existing file name to replace it
                    }
                }
                
                // If no existing cover art file found, use the default preference order
                string[] preferredNames = { "cover.jpg", "folder.jpg", "front.jpg", "albumart.jpg", "album.jpg" };
                return preferredNames[0]; // Default to cover.jpg for new files
            }

            // Fallback if folder path is empty
            return "cover.jpg";
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
            {
                TID3Logger.Warning("Images", "BitmapImage is null, cannot convert to byte array", component: "TagService");
                return null;
            }

            using var scope = TID3Logger.BeginScope("Images", "BitmapImageToByteArray", component: "TagService");

            try
            {
                TID3Logger.Debug("Images", "Converting BitmapImage to byte array", new {
                    HasStreamSource = bitmapImage.StreamSource != null,
                    CanReadStream = bitmapImage.StreamSource?.CanRead ?? false,
                    PixelWidth = bitmapImage.PixelWidth,
                    PixelHeight = bitmapImage.PixelHeight,
                    IsFrozen = bitmapImage.IsFrozen
                }, "TagService");

                // First try to get stored original bytes (best quality)
                var originalBytes = ImageHelper.GetOriginalImageBytes(bitmapImage);
                if (originalBytes != null && originalBytes.Length > 0)
                {
                    TID3Logger.Debug("Images", "Using stored original image bytes", new { 
                        ByteCount = originalBytes.Length 
                    }, "TagService");
                    return originalBytes;
                }

                // Try to get the original source stream if available
                if (bitmapImage.StreamSource != null && bitmapImage.StreamSource.CanRead)
                {
                    try
                    {
                        bitmapImage.StreamSource.Position = 0;
                        using var memoryStream = new MemoryStream();
                        bitmapImage.StreamSource.CopyTo(memoryStream);
                        var bytes = memoryStream.ToArray();
                        TID3Logger.Debug("Images", "Successfully extracted bytes from StreamSource", new { 
                            ByteCount = bytes.Length 
                        }, "TagService");
                        return bytes;
                    }
                    catch (Exception streamEx)
                    {
                        TID3Logger.Warning("Images", "Failed to read from StreamSource, falling back to encoding", streamEx, component: "TagService");
                    }
                }
                else
                {
                    TID3Logger.Debug("Images", "StreamSource not available, using encoder fallback", component: "TagService");
                }

                // Fallback: encode as JPEG
                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                encoder.QualityLevel = 95; // High quality
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapImage));
                
                using var stream = new MemoryStream();
                encoder.Save(stream);
                var encodedBytes = stream.ToArray();
                
                TID3Logger.Debug("Images", "Successfully encoded BitmapImage to JPEG", new { 
                    ByteCount = encodedBytes.Length,
                    QualityLevel = encoder.QualityLevel
                }, "TagService");
                
                return encodedBytes;
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Images", "Error converting bitmap to byte array", ex, new {
                    HasStreamSource = bitmapImage.StreamSource != null,
                    IsFrozen = bitmapImage.IsFrozen,
                    PixelWidth = bitmapImage.PixelWidth,
                    PixelHeight = bitmapImage.PixelHeight
                }, "TagService");
                return null;
            }
        }

        /// <summary>
        /// Securely combines paths while preventing directory traversal attacks
        /// </summary>
        /// <param name="basePath">The base directory path</param>
        /// <param name="relativePath">The relative path to combine</param>
        /// <returns>Combined path if safe, null if path traversal detected</returns>
        private static string? SecureCombinePath(string basePath, string relativePath)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(relativePath))
                return null;

            try
            {
                // Normalize the base path
                basePath = Path.GetFullPath(basePath);
                
                // Sanitize the relative path - remove any path traversal attempts
                relativePath = relativePath.Replace("..", "").Replace("/", "\\");
                relativePath = Path.GetFileName(relativePath); // Only allow filename, no subdirectories
                
                // Combine and normalize the full path
                string combinedPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
                
                // Ensure the combined path is still within the base directory
                if (!combinedPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    !combinedPath.Equals(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    TID3Logger.Warning("Security", "Path traversal attempt blocked", new { RelativePath = relativePath }, "TagService");
                    return null;
                }
                
                return combinedPath;
            }
            catch (Exception ex)
            {
                TID3Logger.Error("Security", "Error in SecureCombinePath", ex, component: "TagService");
                return null;
            }
        }
    }
}