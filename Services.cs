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
using TagLib;

namespace TID3
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
                            TrackCount = GetDiscogsTrackCount(result)
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
                    var artists = new List<string>();
                    foreach (var artist in artistsElement.EnumerateArray())
                    {
                        if (artist.TryGetProperty("name", out var nameElement))
                        {
                            var artistName = nameElement.GetString();
                            if (!string.IsNullOrEmpty(artistName))
                                artists.Add(artistName);
                        }
                    }
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
                    var artists = new List<string>();
                    foreach (var artist in artistElement.EnumerateArray())
                    {
                        if (artist.ValueKind == JsonValueKind.String)
                        {
                            var artistName = artist.GetString();
                            if (!string.IsNullOrEmpty(artistName))
                                artists.Add(artistName);
                        }
                    }
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
                var genres = new List<string>();
                foreach (var genre in genreElement.EnumerateArray())
                {
                    if (genre.ValueKind == JsonValueKind.String)
                    {
                        var genreName = genre.GetString();
                        if (!string.IsNullOrEmpty(genreName))
                            genres.Add(genreName);
                    }
                }
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
                    FileSize = FormatFileSize(new FileInfo(filePath).Length)
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

        public bool SaveFile(AudioFileInfo audioFile)
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
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file {audioFile.FilePath}: {ex.Message}");
                return false;
            }
        }

        public void BatchUpdate(IEnumerable<AudioFileInfo> files, AudioFileInfo template)
        {
            foreach (var file in files)
            {
                if (!string.IsNullOrEmpty(template.Album)) file.Album = template.Album;
                if (!string.IsNullOrEmpty(template.AlbumArtist)) file.AlbumArtist = template.AlbumArtist;
                if (!string.IsNullOrEmpty(template.Genre)) file.Genre = template.Genre;
                if (template.Year > 0) file.Year = template.Year;
                SaveFile(file);
            }
        }
    }
}