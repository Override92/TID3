// Services.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;
using TagLib;

namespace TID3
{
    public class MusicBrainzService
    {
        private static readonly HttpClient client = new HttpClient();
        private const string BASE_URL = "https://musicbrainz.org/ws/2/";
        private readonly SettingsManager _settingsManager;
        private AppSettings _settings;

        public MusicBrainzService()
        {
            _settingsManager = new SettingsManager();
            _settings = _settingsManager.LoadSettings();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", _settings.GetUserAgent());
        }

        public async Task<List<MusicBrainzRelease>> SearchReleases(string query)
        {
            try
            {
                var url = $"{BASE_URL}release/?query={Uri.EscapeDataString(query)}&fmt=json&limit=10";
                var response = await client.GetStringAsync(url);
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
                return new List<MusicBrainzRelease>();
            }
        }

        public async Task<MusicBrainzRelease?> GetReleaseDetails(string releaseId)
        {
            try
            {
                var url = $"{BASE_URL}release/{releaseId}?inc=recordings&fmt=json";
                var response = await client.GetStringAsync(url);
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
            _settings = _settingsManager.LoadSettings();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", _settings.GetUserAgent());
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
        private static readonly HttpClient client = new HttpClient();
        private const string BASE_URL = "https://api.discogs.com/";
        private readonly SettingsManager _settingsManager;
        private AppSettings _settings;

        public DiscogsService()
        {
            _settingsManager = new SettingsManager();
            _settings = _settingsManager.LoadSettings();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", _settings.GetUserAgent());
        }

        public async Task<List<DiscogsRelease>> SearchReleases(string query)
        {
            try
            {
                if (!_settings.HasValidDiscogsCredentials())
                {
                    MessageBox.Show("Discogs API credentials are not configured. Please check Settings.", "API Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return new List<DiscogsRelease>();
                }

                var url = $"{BASE_URL}database/search?q={Uri.EscapeDataString(query)}&type=release&key={_settings.DiscogsApiKey}&secret={_settings.DiscogsSecret}";
                var response = await client.GetStringAsync(url);
                using var document = JsonDocument.Parse(response);
                var data = document.RootElement;

                var releases = new List<DiscogsRelease>();
                if (data.TryGetProperty("results", out var resultsElement))
                {
                    foreach (var result in resultsElement.EnumerateArray())
                    {
                        releases.Add(new DiscogsRelease
                        {
                            Id = result.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                            Title = result.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
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
                return new List<DiscogsRelease>();
            }
        }

        public void RefreshSettings()
        {
            _settings = _settingsManager.LoadSettings();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", _settings.GetUserAgent());
        }

        private static string GetDiscogsArtistString(JsonElement element)
        {
            if (element.TryGetProperty("artist", out var artistElement) && artistElement.ValueKind == JsonValueKind.Array)
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
                return artists.Count > 0 ? string.Join(", ", artists) : "Unknown Artist";
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
            // Check if tracklist is available in search results
            if (element.TryGetProperty("tracklist", out var tracklistElement) && tracklistElement.ValueKind == JsonValueKind.Array)
            {
                return tracklistElement.GetArrayLength();
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
                        var trackMatch = System.Text.RegularExpressions.Regex.Match(formatStr, @"(\d+)\s*tracks?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (trackMatch.Success && int.TryParse(trackMatch.Groups[1].Value, out int trackCount))
                        {
                            return trackCount;
                        }
                    }
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
                using (var file = TagLib.File.Create(filePath))
                {
                    var audioFile = new AudioFileInfo
                    {
                        FilePath = filePath,
                        Title = file.Tag.Title ?? "",
                        Artist = string.Join(", ", file.Tag.Performers ?? new string[0]),
                        Album = file.Tag.Album ?? "",
                        Genre = string.Join(", ", file.Tag.Genres ?? new string[0]),
                        Year = file.Tag.Year,
                        Track = file.Tag.Track,
                        AlbumArtist = string.Join(", ", file.Tag.AlbumArtists ?? new string[0]),
                        Comment = file.Tag.Comment ?? "",
                        Duration = file.Properties.Duration.ToString(@"mm\:ss"),
                        Bitrate = $"{file.Properties.AudioBitrate} kbps",
                        FileSize = FormatFileSize(new FileInfo(filePath).Length)
                    };
                    return audioFile;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file {filePath}: {ex.Message}");
                return null;
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
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
                using (var file = TagLib.File.Create(audioFile.FilePath))
                {
                    file.Tag.Title = audioFile.Title;
                    file.Tag.Performers = audioFile.Artist.Split(',').Select(s => s.Trim()).ToArray();
                    file.Tag.Album = audioFile.Album;
                    file.Tag.Genres = audioFile.Genre.Split(',').Select(s => s.Trim()).ToArray();
                    file.Tag.Year = audioFile.Year;
                    file.Tag.Track = audioFile.Track;
                    file.Tag.AlbumArtists = audioFile.AlbumArtist.Split(',').Select(s => s.Trim()).ToArray();
                    file.Tag.Comment = audioFile.Comment;
                    file.Save();
                    return true;
                }
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