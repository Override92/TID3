// AcoustIdResponseMapper.cs - Pure mapping of AcoustID lookup responses to results.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TID3.Services
{
    /// <summary>
    /// Converts a deserialized <see cref="AcoustIdApiResponse"/> into
    /// <see cref="AcoustIdResult"/> objects. Pure (no HTTP/UI); the optional
    /// MusicBrainz album enrichment is applied separately by the caller as a
    /// post-pass over results whose album is still unknown.
    /// </summary>
    public static class AcoustIdResponseMapper
    {
        public const string UnknownArtist = "Unknown Artist";
        public const string UnknownAlbum = "Unknown Album";
        public const string UnknownTitle = "Unknown Title";

        private const int MaxResults = 5;
        private const int MaxRecordingsPerResult = 3;

        /// <summary>
        /// Maps the response to results, ordered by descending score. Each AcoustID
        /// result contributes up to 3 recordings; a result with no recordings yields
        /// a single placeholder pointing the user at the MusicBrainz search.
        /// </summary>
        public static List<AcoustIdResult> MapResults(AcoustIdApiResponse apiResponse, int durationFallback)
        {
            var results = new List<AcoustIdResult>();
            if (apiResponse.Results == null) return results;

            foreach (var result in apiResponse.Results.Take(MaxResults))
            {
                if (result.Recordings != null && result.Recordings.Length > 0)
                {
                    foreach (var recording in result.Recordings.Take(MaxRecordingsPerResult))
                    {
                        results.Add(new AcoustIdResult
                        {
                            TrackId = result.Id,
                            MusicBrainzId = recording.Id,
                            Title = recording.Title ?? UnknownTitle,
                            Artist = recording.Artists?.FirstOrDefault()?.Name ?? UnknownArtist,
                            Album = recording.Releases?.FirstOrDefault()?.Title ?? UnknownAlbum,
                            Duration = ParseDuration(recording.Duration, durationFallback),
                            Score = result.Score
                        });
                    }
                }
                else
                {
                    results.Add(new AcoustIdResult
                    {
                        TrackId = result.Id,
                        MusicBrainzId = result.Id,
                        Title = $"🎵 Match ({result.Score * 100:0.##}%)",
                        Artist = "Use 'Search MusicBrainz' button for metadata",
                        Album = $"AcoustID: {(result.Id?.Length >= 8 ? result.Id[..8] : result.Id ?? "Unknown")}...",
                        Duration = durationFallback,
                        Score = result.Score
                    });
                }
            }

            return [.. results.OrderByDescending(r => r.Score)];
        }

        /// <summary>
        /// AcoustID returns a recording's duration as a JSON number that may surface
        /// as a double, int, or (via System.Text.Json) a JsonElement whose text is
        /// invariant-formatted. Parses it to whole seconds, falling back to
        /// <paramref name="fallback"/> when null or unparseable.
        /// </summary>
        public static int ParseDuration(object? duration, int fallback)
        {
            if (duration == null)
                return fallback;

            if (duration is double doubleDuration)
                return (int)Math.Round(doubleDuration);

            if (duration is int intDuration)
                return intDuration;

            // The string form (e.g. JsonElement number text) always uses '.' as the
            // decimal separator, so parse with the invariant culture, not the OS locale.
            if (double.TryParse(duration.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDuration))
                return (int)Math.Round(parsedDuration);

            return fallback;
        }
    }
}
