// MusicBrainzResponseParser.cs - Pure parsing of MusicBrainz ws/2 JSON responses.
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TID3.Models;

namespace TID3.Services
{
    /// <summary>
    /// Converts MusicBrainz <c>release</c> search and lookup JSON into
    /// <see cref="MusicBrainzRelease"/> objects. Pure and free of HTTP/UI
    /// dependencies, so it can be unit tested directly against sample JSON.
    /// </summary>
    public static class MusicBrainzResponseParser
    {
        /// <summary>Parses a release search response (the <c>releases</c> array).</summary>
        public static List<MusicBrainzRelease> ParseSearchResults(JsonElement root)
        {
            var releases = new List<MusicBrainzRelease>();
            if (root.TryGetProperty("releases", out var releasesElement))
            {
                foreach (var release in releasesElement.EnumerateArray())
                {
                    releases.Add(new MusicBrainzRelease
                    {
                        Id = release.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Title = release.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "",
                        Artist = GetArtistFromCredit(release),
                        Date = release.TryGetProperty("date", out var date) ? date.GetString() ?? "" : "",
                        Score = release.TryGetProperty("score", out var score) ? score.GetInt32() : 0,
                        TrackCount = GetTrackCount(release)
                    });
                }
            }
            return releases;
        }

        /// <summary>
        /// Parses a single release lookup response (<c>?inc=recordings</c>), including
        /// its tracks. <see cref="MusicBrainzRelease.TrackCount"/> is set to the number
        /// of tracks actually parsed.
        /// </summary>
        public static MusicBrainzRelease ParseReleaseDetails(JsonElement data)
        {
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

            // Track count reflects the tracks actually loaded.
            release.TrackCount = release.Tracks.Count;
            return release;
        }

        /// <summary>Reads the first credited artist name, or "Unknown Artist".</summary>
        public static string GetArtistFromCredit(JsonElement element)
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

        /// <summary>
        /// Reads a track's artist from its recording credit, falling back to the
        /// release artist when the track has no recording-level credit.
        /// </summary>
        public static string GetTrackArtist(JsonElement track, string fallbackArtist)
        {
            if (track.TryGetProperty("recording", out var recording))
            {
                return GetArtistFromCredit(recording);
            }
            return fallbackArtist;
        }

        /// <summary>
        /// Determines a release's track count: the explicit <c>track-count</c> field
        /// if present, otherwise the sum across <c>media</c> entries.
        /// </summary>
        public static int GetTrackCount(JsonElement element)
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
}
