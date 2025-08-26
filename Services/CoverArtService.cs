// CoverArtService.cs - Multi-source cover art retrieval service
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using TID3.Models;
using TID3.Utils;

namespace TID3.Services
{
    public class CoverArtService
    {
        private readonly LastFmService _lastFmService;
        private readonly SpotifyService _spotifyService;
        private readonly ITunesService _iTunesService;
        private readonly DeezerService _deezerService;
        private readonly CoverArtSourceSettings _settings;

        public CoverArtService(CoverArtSourceSettings? settings = null)
        {
            _settings = settings ?? new CoverArtSourceSettings();
            var httpClient = HttpClientManager.CoverArt;
            _lastFmService = new LastFmService(httpClient, _settings.LastFmApiKey);
            _spotifyService = new SpotifyService(httpClient, _settings.SpotifyClientId, _settings.SpotifyClientSecret);
            _iTunesService = new ITunesService(httpClient);
            _deezerService = new DeezerService(httpClient);
        }

        public async Task<List<CoverSource>> SearchCoverArtAsync(string artist, string album)
        {
            Console.WriteLine($"CoverArtService.SearchCoverArtAsync called for: '{artist}' - '{album}'");
            var coverSources = new List<CoverSource>();
            var tasks = new List<Task<CoverSource?>>();

            // Only search enabled sources
            Console.WriteLine($"Checking enabled sources...");
            Console.WriteLine($"LastFm enabled: {_settings.IsSourceEnabled(CoverSourceType.LastFm)}");
            Console.WriteLine($"Spotify enabled: {_settings.IsSourceEnabled(CoverSourceType.Spotify)}");
            Console.WriteLine($"iTunes enabled: {_settings.IsSourceEnabled(CoverSourceType.ITunes)}");
            Console.WriteLine($"Deezer enabled: {_settings.IsSourceEnabled(CoverSourceType.Deezer)}");
            
            if (_settings.IsSourceEnabled(CoverSourceType.LastFm))
            {
                Console.WriteLine("Adding LastFm search task");
                tasks.Add(SearchLastFmCoverAsync(artist, album));
            }
            
            if (_settings.IsSourceEnabled(CoverSourceType.Spotify))
            {
                Console.WriteLine("Adding Spotify search task");
                tasks.Add(SearchSpotifyCoverAsync(artist, album));
            }
            
            if (_settings.IsSourceEnabled(CoverSourceType.ITunes))
            {
                Console.WriteLine("Adding iTunes search task");
                tasks.Add(SearchITunesCoverAsync(artist, album));
            }
            
            if (_settings.IsSourceEnabled(CoverSourceType.Deezer))
            {
                Console.WriteLine("Adding Deezer search task");
                tasks.Add(SearchDeezerCoverAsync(artist, album));
            }

            if (tasks.Count == 0) return coverSources;

            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                if (result != null)
                {
                    coverSources.Add(result);
                }
            }

            // Sort by priority (highest first)
            coverSources.Sort((a, b) => 
                _settings.GetPriority(b.SourceType).CompareTo(_settings.GetPriority(a.SourceType)));

            return coverSources;
        }

        public async Task<CoverSource?> GetBestCoverArtAsync(string artist, string album)
        {
            var sources = await SearchCoverArtAsync(artist, album);
            
            // Return highest priority source with valid image
            foreach (var source in sources)
            {
                if (source.Image != null)
                {
                    return source;
                }
            }

            return null;
        }

        private async Task<CoverSource?> SearchLastFmCoverAsync(string artist, string album)
        {
            try
            {
                var imageUrl = await _lastFmService.GetAlbumCoverAsync(artist, album);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    var image = await LoadImageFromUrlAsync(imageUrl);
                    if (image != null)
                    {
                        return new CoverSource
                        {
                            Name = "Last.fm",
                            Image = image,
                            Source = $"Last.fm - {artist} - {album}",
                            SourceType = CoverSourceType.LastFm,
                            OriginalUrl = imageUrl
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Last.fm cover search failed: {ex.Message}");
            }

            return null;
        }

        private async Task<CoverSource?> SearchSpotifyCoverAsync(string artist, string album)
        {
            try
            {
                var imageUrl = await _spotifyService.GetAlbumCoverAsync(artist, album);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    var image = await LoadImageFromUrlAsync(imageUrl);
                    if (image != null)
                    {
                        return new CoverSource
                        {
                            Name = "Spotify",
                            Image = image,
                            Source = $"Spotify - {artist} - {album}",
                            SourceType = CoverSourceType.Spotify,
                            OriginalUrl = imageUrl
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Spotify cover search failed: {ex.Message}");
            }

            return null;
        }

        private async Task<CoverSource?> SearchITunesCoverAsync(string artist, string album)
        {
            try
            {
                var imageUrl = await _iTunesService.GetAlbumCoverAsync(artist, album);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    var image = await LoadImageFromUrlAsync(imageUrl);
                    if (image != null)
                    {
                        return new CoverSource
                        {
                            Name = "iTunes",
                            Image = image,
                            Source = $"iTunes - {artist} - {album}",
                            SourceType = CoverSourceType.ITunes,
                            OriginalUrl = imageUrl
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes cover search failed: {ex.Message}");
            }

            return null;
        }

        private async Task<CoverSource?> SearchDeezerCoverAsync(string artist, string album)
        {
            try
            {
                var imageUrl = await _deezerService.GetAlbumCoverAsync(artist, album);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    var image = await LoadImageFromUrlAsync(imageUrl);
                    if (image != null)
                    {
                        return new CoverSource
                        {
                            Name = "Deezer",
                            Image = image,
                            Source = $"Deezer - {artist} - {album}",
                            SourceType = CoverSourceType.Deezer,
                            OriginalUrl = imageUrl
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Deezer cover search failed: {ex.Message}");
            }

            return null;
        }

        private async Task<BitmapImage?> LoadImageFromUrlAsync(string imageUrl)
        {
            try
            {
                var response = await HttpClientManager.CoverArt.GetAsync(imageUrl);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load image from {imageUrl}: {ex.Message}");
            }

            return null;
        }

        public void Dispose()
        {
            // HttpClient is now managed by HttpClientManager - no disposal needed
        }
    }

    // Last.fm API Service
    public class LastFmService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BASE_URL = "https://ws.audioscrobbler.com/2.0/";

        public LastFmService(HttpClient httpClient, string apiKey = "")
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
        }

        public async Task<string?> GetAlbumCoverAsync(string artist, string album)
        {
            if (string.IsNullOrEmpty(_apiKey)) return null;
            
            try
            {
                var url = $"{BASE_URL}?method=album.getinfo&api_key={_apiKey}&artist={Uri.EscapeDataString(artist)}&album={Uri.EscapeDataString(album)}&format=json";
                var response = await HttpClientManager.CoverArt.GetStringAsync(url);
                
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("album", out var albumElement) &&
                    albumElement.TryGetProperty("image", out var imageArray))
                {
                    // Get the largest image (last in array)
                    var images = imageArray.EnumerateArray().ToArray();
                    for (int i = images.Length - 1; i >= 0; i--)
                    {
                        var imageElement = images[i];
                        if (imageElement.TryGetProperty("#text", out var urlElement))
                        {
                            var imageUrl = urlElement.GetString();
                            if (!string.IsNullOrEmpty(imageUrl) && !imageUrl.Contains("default_album"))
                            {
                                return imageUrl;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Last.fm API error: {ex.Message}");
            }

            return null;
        }
    }

    // Spotify Web API Service
    public class SpotifyService
    {
        private readonly HttpClient _httpClient;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private const string BASE_URL = "https://api.spotify.com/v1/";
        private string? _accessToken;
        private DateTime _tokenExpiry;

        public SpotifyService(HttpClient httpClient, string clientId = "", string clientSecret = "")
        {
            _httpClient = httpClient;
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        public async Task<string?> GetAlbumCoverAsync(string artist, string album)
        {
            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret)) return null;
            
            try
            {
                await EnsureValidTokenAsync();
                if (string.IsNullOrEmpty(_accessToken)) return null;

                var query = Uri.EscapeDataString($"artist:{artist} album:{album}");
                var url = $"{BASE_URL}search?q={query}&type=album&limit=10"; // Get more results for better matching
                
                System.Diagnostics.Debug.WriteLine($"Spotify search URL: {url}");
                System.Diagnostics.Debug.WriteLine($"Searching for: Artist='{artist}', Album='{album}'");
                Console.WriteLine($"Spotify search URL: {url}");
                Console.WriteLine($"Searching for: Artist='{artist}', Album='{album}'");
                
                // Write to log file for debugging
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TID3_Debug.log");
                File.AppendAllText(logPath, $"\n=== SPOTIFY SEARCH ===\n");
                File.AppendAllText(logPath, $"Search URL: {url}\n");
                File.AppendAllText(logPath, $"Searching for: Artist='{artist}', Album='{album}'\n");
                
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
                
                var httpResponse = await HttpClientManager.CoverArt.SendAsync(request);
                var response = await httpResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(response);
                
                if (doc.RootElement.TryGetProperty("albums", out var albums) &&
                    albums.TryGetProperty("items", out var items) &&
                    items.GetArrayLength() > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Spotify returned {items.GetArrayLength()} album results");
                    Console.WriteLine($"Spotify returned {items.GetArrayLength()} album results");
                    
                    File.AppendAllText(logPath, $"Spotify returned {items.GetArrayLength()} album results\n");
                    
                    // Find the best matching album
                    foreach (var albumElement in items.EnumerateArray())
                    {
                        if (albumElement.TryGetProperty("name", out var albumNameElement) &&
                            albumElement.TryGetProperty("artists", out var artistsArray))
                        {
                            var albumName = albumNameElement.GetString() ?? "";
                            var artistFound = false;
                            var matchingArtistName = "";
                            
                            // Check if any artist matches
                            foreach (var artistElement in artistsArray.EnumerateArray())
                            {
                                if (artistElement.TryGetProperty("name", out var artistNameElement))
                                {
                                    var artistName = artistNameElement.GetString() ?? "";
                                    System.Diagnostics.Debug.WriteLine($"  Result: '{albumName}' by '{artistName}'");
                                    Console.WriteLine($"  Result: '{albumName}' by '{artistName}'");
                                    
                                    File.AppendAllText(logPath, $"  Result: '{albumName}' by '{artistName}'\n");
                                    
                                    if (IsGoodMatch(artist, artistName))
                                    {
                                        artistFound = true;
                                        matchingArtistName = artistName;
                                        System.Diagnostics.Debug.WriteLine($"    Artist match: '{artist}' ≈ '{artistName}'");
                                        break;
                                    }
                                    else
                                    {
                                        System.Diagnostics.Debug.WriteLine($"    Artist no match: '{artist}' ≠ '{artistName}'");
                                    }
                                }
                            }
                            
                            // Check album name matching
                            var albumMatches = IsGoodMatch(album, albumName);
                            if (albumMatches)
                            {
                                System.Diagnostics.Debug.WriteLine($"    Album match: '{album}' ≈ '{albumName}'");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"    Album no match: '{album}' ≠ '{albumName}'");
                            }
                            
                            // If artist matches and album name is a good match
                            if (artistFound && albumMatches)
                            {
                                if (albumElement.TryGetProperty("images", out var images) &&
                                    images.GetArrayLength() > 0)
                                {
                                    var firstImage = images[0];
                                    if (firstImage.TryGetProperty("url", out var urlElement))
                                    {
                                        System.Diagnostics.Debug.WriteLine($"✓ Spotify match found: '{albumName}' by '{matchingArtistName}'");
                                        Console.WriteLine($"✓ Spotify match found: '{albumName}' by '{matchingArtistName}'");
                                        
                                        File.AppendAllText(logPath, $"✓ SPOTIFY MATCH FOUND: '{albumName}' by '{matchingArtistName}'\n");
                                        return urlElement.GetString();
                                    }
                                }
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"✗ Spotify: No good match found for '{album}' by '{artist}'");
                    Console.WriteLine($"✗ Spotify: No good match found for '{album}' by '{artist}'");
                    
                    var logPath2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TID3_Debug.log");
                    File.AppendAllText(logPath2, $"✗ SPOTIFY: No good match found for '{album}' by '{artist}'\n");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Spotify API error: {ex.Message}");
            }

            return null;
        }

        private async Task EnsureValidTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
                return;

            try
            {
                // Implement Spotify Client Credentials OAuth flow
                var authString = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                tokenRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authString);
                tokenRequest.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });

                var tokenResponse = await HttpClientManager.CoverArt.SendAsync(tokenRequest);
                
                if (tokenResponse.IsSuccessStatusCode)
                {
                    var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(tokenJson);
                    
                    if (doc.RootElement.TryGetProperty("access_token", out var tokenElement) &&
                        doc.RootElement.TryGetProperty("expires_in", out var expiresInElement))
                    {
                        _accessToken = tokenElement.GetString();
                        var expiresInSeconds = expiresInElement.GetInt32();
                        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresInSeconds - 30); // 30 second buffer
                        
                        System.Diagnostics.Debug.WriteLine($"Spotify token acquired, expires at: {_tokenExpiry}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Spotify token request failed: {tokenResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Spotify token request error: {ex.Message}");
            }
        }

        private static bool IsGoodMatch(string expected, string actual)
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TID3_Debug.log");
            
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            {
                System.Diagnostics.Debug.WriteLine($"      IsGoodMatch: Empty string - '{expected}' vs '{actual}' = false");
                return false;
            }

            // Normalize strings for comparison
            var normalizedExpected = NormalizeForComparison(expected);
            var normalizedActual = NormalizeForComparison(actual);
            
            System.Diagnostics.Debug.WriteLine($"      IsGoodMatch: Normalized '{expected}' → '{normalizedExpected}', '{actual}' → '{normalizedActual}'");
            File.AppendAllText(logPath, $"      Matching: '{expected}' vs '{actual}'\n");

            // Exact match (case-insensitive)
            if (normalizedExpected.Equals(normalizedActual, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"      IsGoodMatch: Exact match = true");
                return true;
            }

            // Check if actual starts with expected (for cases like "Lasso" vs "Lasso - Deluxe Edition")
            // But exclude remix albums, live albums, etc.
            if (normalizedActual.StartsWith(normalizedExpected + " ", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = normalizedActual.Substring(normalizedExpected.Length + 1).ToLowerInvariant();
                
                // Reject if it's clearly a different type of album
                var rejectKeywords = new[] { "remix", "remixe", "live", "acoustic", "instrumental", "karaoke", "cover" };
                if (rejectKeywords.Any(keyword => suffix.Contains(keyword)))
                {
                    System.Diagnostics.Debug.WriteLine($"      IsGoodMatch: Starts with match but contains reject keyword '{suffix}' = false");
                    File.AppendAllText(logPath, $"      REJECTED: '{normalizedActual}' contains reject keyword in '{suffix}'\n");
                    return false;
                }
                
                System.Diagnostics.Debug.WriteLine($"      IsGoodMatch: Starts with match = true");
                File.AppendAllText(logPath, $"      ACCEPTED: '{normalizedActual}' starts with '{normalizedExpected}'\n");
                return true;
            }

            // Check for very close matches (allowing for minor differences like punctuation)
            var expectedWords = normalizedExpected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var actualWords = normalizedActual.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // If the expected has multiple words, all should be present in actual
            if (expectedWords.Length > 1)
            {
                var result = expectedWords.All(word => 
                    actualWords.Any(actualWord => 
                        actualWord.Equals(word, StringComparison.OrdinalIgnoreCase)));
                System.Diagnostics.Debug.WriteLine($"      IsGoodMatch: Multi-word match ({expectedWords.Length} words) = {result}");
                return result;
            }

            // For single words, be more strict to avoid "Lasso" matching "Lasso-Remixe"
            System.Diagnostics.Debug.WriteLine($"      IsGoodMatch: Single word, strict match = false");
            return false;
        }

        private static string NormalizeForComparison(string input)
        {
            // Remove common punctuation and normalize spaces
            return input.Replace("-", " ")
                       .Replace("_", " ")
                       .Replace(".", " ")
                       .Replace(",", " ")
                       .Replace("  ", " ")
                       .Trim();
        }
    }

    // iTunes Search API Service
    public class ITunesService
    {
        private readonly HttpClient _httpClient;
        private const string BASE_URL = "https://itunes.apple.com/search";

        public ITunesService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string?> GetAlbumCoverAsync(string artist, string album)
        {
            try
            {
                var term = Uri.EscapeDataString($"{artist} {album}");
                var url = $"{BASE_URL}?term={term}&entity=album&limit=20"; // Get more results for better matching
                
                System.Diagnostics.Debug.WriteLine($"iTunes API call: {url}");
                var response = await HttpClientManager.CoverArt.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                
                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    // Find the best matching album
                    foreach (var result in results.EnumerateArray())
                    {
                        if (result.TryGetProperty("collectionName", out var albumNameElement) &&
                            result.TryGetProperty("artistName", out var artistNameElement) &&
                            result.TryGetProperty("artworkUrl100", out var artworkElement))
                        {
                            var albumName = albumNameElement.GetString() ?? "";
                            var artistName = artistNameElement.GetString() ?? "";
                            var artworkUrl = artworkElement.GetString();
                            
                            // Check for good match
                            if (IsGoodMatch(artist, artistName) && IsGoodMatch(album, albumName) && !string.IsNullOrEmpty(artworkUrl))
                            {
                                System.Diagnostics.Debug.WriteLine($"iTunes match: '{albumName}' by '{artistName}' (requested: '{album}' by '{artist}')");
                                // Convert to high resolution by replacing "100x100" with "600x600"
                                return artworkUrl.Replace("100x100", "600x600");
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"iTunes: No good match found for '{album}' by '{artist}'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iTunes API error: {ex.Message}");
            }

            return null;
        }

        private static bool IsGoodMatch(string expected, string actual)
        {
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
                return false;

            // Normalize strings for comparison
            var normalizedExpected = NormalizeForComparison(expected);
            var normalizedActual = NormalizeForComparison(actual);

            // Exact match (case-insensitive)
            if (normalizedExpected.Equals(normalizedActual, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check if actual starts with expected (for cases like "Lasso" vs "Lasso - Deluxe Edition")
            if (normalizedActual.StartsWith(normalizedExpected + " ", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check for very close matches (allowing for minor differences like punctuation)
            var expectedWords = normalizedExpected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var actualWords = normalizedActual.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // If the expected has multiple words, all should be present in actual
            if (expectedWords.Length > 1)
            {
                return expectedWords.All(word => 
                    actualWords.Any(actualWord => 
                        actualWord.Equals(word, StringComparison.OrdinalIgnoreCase)));
            }

            // For single words, be more strict to avoid "Lasso" matching "Lasso-Remixe"
            return false;
        }

        private static string NormalizeForComparison(string input)
        {
            // Remove common punctuation and normalize spaces
            return input.Replace("-", " ")
                       .Replace("_", " ")
                       .Replace(".", " ")
                       .Replace(",", " ")
                       .Replace("  ", " ")
                       .Trim();
        }
    }

    // Deezer API Service
    public class DeezerService
    {
        private readonly HttpClient _httpClient;
        private const string BASE_URL = "https://api.deezer.com/";

        public DeezerService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string?> GetAlbumCoverAsync(string artist, string album)
        {
            try
            {
                var query = Uri.EscapeDataString($"{artist} {album}");
                var url = $"{BASE_URL}search/album?q={query}&limit=20"; // Get more results for better matching
                
                System.Diagnostics.Debug.WriteLine($"Deezer API call: {url}");
                var response = await HttpClientManager.CoverArt.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    // Find the best matching album
                    foreach (var result in data.EnumerateArray())
                    {
                        if (result.TryGetProperty("title", out var albumNameElement) &&
                            result.TryGetProperty("artist", out var artistElement) &&
                            result.TryGetProperty("cover_xl", out var coverElement))
                        {
                            var albumName = albumNameElement.GetString() ?? "";
                            var coverUrl = coverElement.GetString();
                            
                            // Get artist name from artist object
                            var artistName = "";
                            if (artistElement.TryGetProperty("name", out var artistNameElement))
                            {
                                artistName = artistNameElement.GetString() ?? "";
                            }
                            
                            // Check for good match
                            if (IsGoodMatch(artist, artistName) && IsGoodMatch(album, albumName) && !string.IsNullOrEmpty(coverUrl))
                            {
                                System.Diagnostics.Debug.WriteLine($"Deezer match: '{albumName}' by '{artistName}' (requested: '{album}' by '{artist}')");
                                return coverUrl;
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Deezer: No good match found for '{album}' by '{artist}'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Deezer API error: {ex.Message}");
            }

            return null;
        }

        private static bool IsGoodMatch(string expected, string actual)
        {
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
                return false;

            // Normalize strings for comparison
            var normalizedExpected = NormalizeForComparison(expected);
            var normalizedActual = NormalizeForComparison(actual);

            // Exact match (case-insensitive)
            if (normalizedExpected.Equals(normalizedActual, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check if actual starts with expected (for cases like "Lasso" vs "Lasso - Deluxe Edition")
            if (normalizedActual.StartsWith(normalizedExpected + " ", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check for very close matches (allowing for minor differences like punctuation)
            var expectedWords = normalizedExpected.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var actualWords = normalizedActual.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // If the expected has multiple words, all should be present in actual
            if (expectedWords.Length > 1)
            {
                return expectedWords.All(word => 
                    actualWords.Any(actualWord => 
                        actualWord.Equals(word, StringComparison.OrdinalIgnoreCase)));
            }

            // For single words, be more strict to avoid "Lasso" matching "Lasso-Remixe"
            return false;
        }

        private static string NormalizeForComparison(string input)
        {
            // Remove common punctuation and normalize spaces
            return input.Replace("-", " ")
                       .Replace("_", " ")
                       .Replace(".", " ")
                       .Replace(",", " ")
                       .Replace("  ", " ")
                       .Trim();
        }
    }
}