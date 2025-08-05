using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TID3
{
    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public System.DateTime PublishedAt { get; set; }
        public bool IsPrerelease { get; set; }
    }

    public class UpdateService
    {
        private readonly HttpClient _httpClient;
        private const string GITHUB_API_URL = "https://api.github.com/repos/Override92/TID3/releases/latest";
        private const string USER_AGENT = "TID3-UpdateChecker/1.0";

        public UpdateService()
        {
            _httpClient = HttpClientManager.CreateClientWithUserAgent(USER_AGENT);
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(GITHUB_API_URL);
                
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var releaseData = JsonSerializer.Deserialize<JsonElement>(jsonString);

                var updateInfo = new UpdateInfo
                {
                    Version = releaseData.TryGetProperty("tag_name", out var tagName) 
                        ? CleanVersionString(tagName.GetString() ?? "") 
                        : "",
                    DownloadUrl = releaseData.TryGetProperty("html_url", out var htmlUrl) 
                        ? htmlUrl.GetString() ?? "" 
                        : "",
                    ReleaseNotes = releaseData.TryGetProperty("body", out var body) 
                        ? body.GetString() ?? "" 
                        : "",
                    IsPrerelease = releaseData.TryGetProperty("prerelease", out var prerelease) 
                        && prerelease.GetBoolean()
                };

                if (releaseData.TryGetProperty("published_at", out var publishedAt) 
                    && System.DateTime.TryParse(publishedAt.GetString(), out var parsedDate))
                {
                    updateInfo.PublishedAt = parsedDate;
                }

                return updateInfo;
            }
            catch
            {
                return null;
            }
        }

        public bool IsUpdateAvailable(string currentVersion, UpdateInfo updateInfo)
        {
            if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(updateInfo.Version))
                return false;

            var current = ParseVersion(currentVersion);
            var latest = ParseVersion(updateInfo.Version);

            return CompareVersions(latest, current) > 0;
        }

        public bool ShouldCheckForUpdates(System.DateTime lastCheck)
        {
            return System.DateTime.Now - lastCheck > System.TimeSpan.FromDays(1);
        }

        private string CleanVersionString(string version)
        {
            // Remove 'v' prefix if present (e.g., "v1.2.3" -> "1.2.3")
            return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
                ? version.Substring(1) 
                : version;
        }

        private System.Version ParseVersion(string versionString)
        {
            var cleanVersion = CleanVersionString(versionString);
            
            // Handle semantic versions with pre-release identifiers
            var match = Regex.Match(cleanVersion, @"(\d+)\.(\d+)\.(\d+)");
            if (match.Success)
            {
                var major = int.Parse(match.Groups[1].Value);
                var minor = int.Parse(match.Groups[2].Value);
                var patch = int.Parse(match.Groups[3].Value);
                return new System.Version(major, minor, patch);
            }

            // Fallback to default parsing
            if (System.Version.TryParse(cleanVersion, out var version))
                return version;

            return new System.Version(0, 0, 0);
        }

        private int CompareVersions(System.Version version1, System.Version version2)
        {
            return version1.CompareTo(version2);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}