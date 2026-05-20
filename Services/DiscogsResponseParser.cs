// DiscogsResponseParser.cs - Pure parsing of Discogs search-API JSON responses.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TID3.Models;

namespace TID3.Services
{
    /// <summary>
    /// Converts Discogs <c>database/search</c> JSON into <see cref="DiscogsRelease"/>
    /// objects. Pure and free of HTTP/UI dependencies, so it can be unit tested
    /// directly against sample JSON.
    /// </summary>
    public static class DiscogsResponseParser
    {
        // Trailing Discogs disambiguation index, e.g. the " (2)" in "Nirvana (2)".
        private static readonly Regex DisambiguationSuffix =
            new(@"\s*\(\d+\)$", RegexOptions.Compiled);

        /// <summary>
        /// Removes the trailing Discogs disambiguation index from an artist name
        /// (e.g. "Nirvana (2)" -> "Nirvana"). Discogs adds these to tell apart
        /// distinct artists that share a name; they are not part of the real name.
        /// </summary>
        public static string StripArtistDisambiguation(string artist)
            => DisambiguationSuffix.Replace(artist, "").Trim();

        /// <summary>Parses the full search response (the <c>results</c> array).</summary>
        public static List<DiscogsRelease> ParseSearchResults(JsonElement root)
        {
            var releases = new List<DiscogsRelease>();
            if (root.TryGetProperty("results", out var resultsElement))
            {
                foreach (var result in resultsElement.EnumerateArray())
                {
                    releases.Add(ParseRelease(result));
                }
            }
            return releases;
        }

        /// <summary>Parses a single release object from the <c>results</c> array.</summary>
        public static DiscogsRelease ParseRelease(JsonElement result)
        {
            var titleString = result.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "";

            return new DiscogsRelease
            {
                Id = result.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                Title = ExtractAlbumFromTitle(titleString),
                Artist = GetArtist(result),
                Year = GetYear(result),
                Genre = GetGenre(result),
                TrackCount = GetTrackCount(result),
                CoverArtUrl = GetCoverArtUrl(result)
            };
        }

        public static string GetArtist(JsonElement element)
        {
            // Try to get artist from basic_information first (common in search results)
            if (element.TryGetProperty("basic_information", out var basicInfo))
            {
                if (basicInfo.TryGetProperty("artists", out var artistsElement) && artistsElement.ValueKind == JsonValueKind.Array)
                {
                    var artists = artistsElement.EnumerateArray()
                        .Where(artist => artist.TryGetProperty("name", out var nameElement) && !string.IsNullOrEmpty(nameElement.GetString()))
                        .Select(artist => StripArtistDisambiguation(artist.GetProperty("name").GetString()!))
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
                        return StripArtistDisambiguation(artistName);
                }
                else if (artistElement.ValueKind == JsonValueKind.Array)
                {
                    var artists = artistElement.EnumerateArray()
                        .Where(artist => artist.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(artist.GetString()))
                        .Select(artist => StripArtistDisambiguation(artist.GetString()!))
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
                        return StripArtistDisambiguation(parts[0].Trim());
                    }
                }
            }

            return "Unknown Artist";
        }

        public static string GetGenre(JsonElement element)
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

        /// <summary>
        /// Discogs search titles are typically "Artist - Album"; returns the album part,
        /// or the whole (trimmed) string when there is no separator.
        /// </summary>
        public static string ExtractAlbumFromTitle(string titleString)
        {
            if (string.IsNullOrEmpty(titleString))
                return "";

            if (titleString.Contains(" - "))
            {
                var parts = titleString.Split([" - "], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    return parts[1].Trim();
                }
            }

            return titleString.Trim();
        }

        public static string GetYear(JsonElement element)
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

        public static int GetTrackCount(JsonElement element)
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

        public static string? GetCoverArtUrl(JsonElement element)
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
}
