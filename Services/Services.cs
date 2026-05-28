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
    public class MusicBrainzService : IMusicBrainzService
    {
        private const string BASE_URL = "https://musicbrainz.org/ws/2/";
        private AppSettings _settings;

        public MusicBrainzService()
        {
            _settings = SettingsManager.LoadSettings();
        }

        public async Task<List<MusicBrainzRelease>> SearchReleases(string query)
        {
            var url = $"{BASE_URL}release/?query={Uri.EscapeDataString(query)}&fmt=json&limit=10";
            var response = await HttpClientManager.MusicBrainz.GetStringAsync(url);
            using var document = JsonDocument.Parse(response);

            return MusicBrainzResponseParser.ParseSearchResults(document.RootElement);
        }

        public async Task<MusicBrainzRelease?> GetReleaseDetails(string releaseId)
        {
            var url = $"{BASE_URL}release/{releaseId}?inc=recordings&fmt=json";
            var response = await HttpClientManager.MusicBrainz.GetStringAsync(url);
            using var document = JsonDocument.Parse(response);

            return MusicBrainzResponseParser.ParseReleaseDetails(document.RootElement);
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
    }

    public class DiscogsService : IDiscogsService
    {
        private const string BASE_URL = "https://api.discogs.com/";
        private AppSettings _settings;

        public DiscogsService()
        {
            _settings = SettingsManager.LoadSettings();
        }

        public async Task<List<DiscogsRelease>> SearchReleases(string query)
        {
            if (!_settings.HasValidDiscogsCredentials())
            {
                throw new InvalidOperationException("Discogs API credentials are not configured. Please check Settings.");
            }

            var url = $"{BASE_URL}database/search?q={Uri.EscapeDataString(query)}&type=release";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(
                "Authorization",
                $"Discogs key={_settings.DiscogsApiKey}, secret={_settings.DiscogsSecret}");

            using var response = await HttpClientManager.Discogs.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            return DiscogsResponseParser.ParseSearchResults(document.RootElement);
        }

        public void RefreshSettings()
        {
            _settings = SettingsManager.LoadSettings();
            // Headers are now managed by HttpClientManager.MusicBrainz
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
                    Artist = TagFormatting.JoinValues(tag.Performers),
                    Album = tag.Album ?? string.Empty,
                    Genre = TagFormatting.JoinValues(tag.Genres),
                    Year = tag.Year,
                    Track = tag.Track,
                    AlbumArtist = TagFormatting.JoinValues(tag.AlbumArtists),
                    Comment = tag.Comment ?? string.Empty,
                    Duration = TagFormatting.FormatDuration(properties.Duration),
                    Bitrate = TagFormatting.FormatBitrate(properties.AudioBitrate),
                    FileSize = TagFormatting.FormatFileSize(new FileInfo(filePath).Length),
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
        
        public (bool Success, bool CoverArtSaved) SaveFile(AudioFileInfo audioFile, bool replaceExistingCoverArt = false)
        {
            try
            {
                using var file = TagLib.File.Create(audioFile.FilePath);
                file.Tag.Title = audioFile.Title;
                file.Tag.Performers = TagFormatting.SplitValues(audioFile.Artist);
                file.Tag.Album = audioFile.Album;
                file.Tag.Genres = TagFormatting.SplitValues(audioFile.Genre);
                file.Tag.Year = audioFile.Year;
                file.Tag.Track = audioFile.Track;
                file.Tag.AlbumArtists = TagFormatting.SplitValues(audioFile.AlbumArtist);
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
                TID3Logger.Error("Tags", "Error saving file", ex,
                    new { audioFile.FilePath }, "TagService");
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
                    foreach (var fileName in CoverArtFileResolver.StandardCoverNames)
                    {
                        var coverPath = SecureCombinePath(folderPath, fileName);
                        if (coverPath != null && System.IO.File.Exists(coverPath))
                        {
                            return $"Local file: {fileName}";
                        }
                    }

                    // Check for any image files
                    var imageFiles = Directory.GetFiles(folderPath)
                        .Where(CoverArtFileResolver.IsImageFile)
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

                // Resolve cover-file candidates (standard names first, then keyword
                // images, then any image) and load the first that opens successfully.
                var folderFiles = Directory.GetFiles(folderPath);
                var candidates = CoverArtFileResolver.ResolveCoverCandidates(folderFiles);
                TID3Logger.Debug("Images", "Resolved cover candidates", new { Candidates = candidates }, "TagService");

                foreach (var fileName in candidates)
                {
                    var coverPath = SecureCombinePath(folderPath, fileName);
                    if (coverPath == null || !System.IO.File.Exists(coverPath))
                        continue;

                    TID3Logger.Images.LogFileInfo(coverPath, "TagService");
                    var result = LoadImageFromFile(coverPath);
                    if (result != null)
                    {
                        TID3Logger.Images.LogImageDetails(result, "Folder cover result", "TagService");
                        return result;
                    }

                    TID3Logger.Warning("Images", "Failed to load cover file", new { FilePath = coverPath }, "TagService");
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
                foreach (var fileName in CoverArtFileResolver.StandardCoverNames)
                {
                    var coverPath = SecureCombinePath(folderPath, fileName);
                    if (coverPath != null && System.IO.File.Exists(coverPath))
                    {
                        return $"Folder file: {fileName}";
                    }
                }

                // Check for any image files
                var imageFiles = Directory.GetFiles(folderPath)
                    .Where(CoverArtFileResolver.IsImageFile)
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
            if (string.IsNullOrEmpty(folderPath))
                return "cover.jpg";

            // Reuse an existing standard cover file (to replace in place), else "cover.jpg".
            return CoverArtFileResolver.ResolveSaveFileName(Directory.GetFiles(folderPath));
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