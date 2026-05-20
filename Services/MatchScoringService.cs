// MatchScoringService.cs - Pure scoring logic for ranking online release matches.
using System;
using System.Globalization;
using System.Text;
using TID3.Models;

namespace TID3.Services
{
    /// <summary>
    /// Scores how well an online release (MusicBrainz / Discogs) matches a local
    /// audio file. All methods are pure and free of UI dependencies, so they can
    /// be unit tested directly.
    ///
    /// Weighting: Artist 35%, Album/Title 30%, Track count 20%, Year 10%, Title 5%.
    /// </summary>
    public class MatchScoringService
    {
        private const double ArtistWeight = 0.35;
        private const double AlbumWeight = 0.30;
        private const double TrackCountWeight = 0.20;
        private const double YearWeight = 0.10;
        private const double TitleWeight = 0.05;

        /// <summary>
        /// Scores a MusicBrainz release against a local file. Returns a value in [0, 1].
        /// </summary>
        /// <param name="file">The local audio file, or null (scores 0).</param>
        /// <param name="release">The candidate MusicBrainz release.</param>
        /// <param name="loadedTrackCount">Number of tracks currently loaded, used for track-count comparison.</param>
        public double CalculateMatchScore(AudioFileInfo? file, MusicBrainzRelease release, int loadedTrackCount)
        {
            if (file == null) return 0.0;

            return ScoreCore(
                file,
                releaseArtist: release.Artist,
                releaseTitle: release.Title,
                releaseTrackCount: release.TrackCount,
                releaseYear: ExtractYearFromDate(release.Date),
                loadedTrackCount: loadedTrackCount);
        }

        /// <summary>
        /// Scores a Discogs release against a local file. Returns a value in [0, 1].
        /// </summary>
        /// <param name="file">The local audio file, or null (scores 0).</param>
        /// <param name="release">The candidate Discogs release.</param>
        /// <param name="loadedTrackCount">Number of tracks currently loaded, used for track-count comparison.</param>
        public double CalculateMatchScore(AudioFileInfo? file, DiscogsRelease release, int loadedTrackCount)
        {
            if (file == null) return 0.0;

            return ScoreCore(
                file,
                releaseArtist: release.Artist,
                releaseTitle: release.Title,
                releaseTrackCount: release.TrackCount,
                releaseYear: uint.TryParse(release.Year, out uint y) ? y : 0,
                loadedTrackCount: loadedTrackCount);
        }

        /// <summary>
        /// Shared scoring core. <paramref name="releaseYear"/> is 0 when the release year is unknown.
        /// </summary>
        private double ScoreCore(AudioFileInfo file, string releaseArtist, string releaseTitle,
            int releaseTrackCount, uint releaseYear, int loadedTrackCount)
        {
            double score = 0.0;
            double maxScore = 0.0;

            // Artist match (most important - 35% weight)
            maxScore += ArtistWeight;
            if (!string.IsNullOrEmpty(file.Artist) && !string.IsNullOrEmpty(releaseArtist))
            {
                score += ArtistWeight * CalculateStringSimilarity(file.Artist, releaseArtist);
            }

            // Album/Title match (30% weight)
            maxScore += AlbumWeight;
            if (!string.IsNullOrEmpty(file.Album) && !string.IsNullOrEmpty(releaseTitle))
            {
                score += AlbumWeight * CalculateStringSimilarity(file.Album, releaseTitle);
            }

            // Track count match (20% weight)
            maxScore += TrackCountWeight;
            if (releaseTrackCount > 0 && loadedTrackCount > 0)
            {
                // Perfect match gets full score, with decreasing score for differences
                var trackCountDiff = Math.Abs(releaseTrackCount - loadedTrackCount);
                if (trackCountDiff == 0)
                {
                    score += TrackCountWeight; // Perfect match
                }
                else if (trackCountDiff <= 2)
                {
                    score += TrackCountWeight * (1.0 - trackCountDiff / 3.0); // Partial credit for close matches
                }
                else if (trackCountDiff <= 5)
                {
                    score += TrackCountWeight * 0.3; // Small credit for reasonably close matches
                }
            }

            // Year match (10% weight)
            maxScore += YearWeight;
            if (file.Year > 0 && releaseYear > 0)
            {
                if (releaseYear == file.Year)
                {
                    score += YearWeight;
                }
                else
                {
                    // Partial credit for close years
                    var yearDiff = Math.Abs((int)releaseYear - (int)file.Year);
                    if (yearDiff <= 2) score += YearWeight * (1.0 - yearDiff / 3.0);
                }
            }

            // Title match (5% weight) - for when album is missing
            maxScore += TitleWeight;
            if (!string.IsNullOrEmpty(file.Title) && !string.IsNullOrEmpty(releaseTitle))
            {
                score += TitleWeight * CalculateStringSimilarity(file.Title, releaseTitle);
            }

            return maxScore > 0 ? score / maxScore : 0.0;
        }

        /// <summary>
        /// Returns a similarity score in [0, 1] between two strings: 1.0 exact (after
        /// normalization), 0.8 when one contains the other, otherwise a Levenshtein-based ratio.
        /// </summary>
        public double CalculateStringSimilarity(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
                return 0.0;

            // Normalize strings for comparison
            str1 = NormalizeForComparison(str1);
            str2 = NormalizeForComparison(str2);

            // Exact match
            if (str1.Equals(str2, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            // Contains match
            if (str1.Contains(str2, StringComparison.OrdinalIgnoreCase) ||
                str2.Contains(str1, StringComparison.OrdinalIgnoreCase))
                return 0.8;

            // Calculate Levenshtein distance similarity
            var distance = CalculateLevenshteinDistance(str1, str2);
            var maxLength = Math.Max(str1.Length, str2.Length);

            if (maxLength == 0) return 1.0;

            var similarity = 1.0 - (double)distance / maxLength;
            return Math.Max(0.0, similarity);
        }

        /// <summary>
        /// Lower-cases, folds diacritics, and strips punctuation noise so two titles
        /// can be compared on their essential characters (e.g. "Sigur Rós" == "Sigur Ros").
        /// </summary>
        public string NormalizeForComparison(string input)
        {
            return RemoveDiacritics(input)
                       .ToLowerInvariant()
                       .Replace("&", "and")
                       .Replace("'", "")
                       .Replace("-", " ")
                       .Replace("  ", " ")
                       .Trim();
        }

        /// <summary>
        /// Removes combining diacritical marks (é -> e, ö -> o, ñ -> n, …) by
        /// decomposing to Unicode form D, dropping non-spacing marks, and recomposing.
        /// </summary>
        private static string RemoveDiacritics(string text)
        {
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);

            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Classic Levenshtein edit distance between two strings.
        /// </summary>
        public int CalculateLevenshteinDistance(string str1, string str2)
        {
            var len1 = str1.Length;
            var len2 = str2.Length;
            var matrix = new int[len1 + 1, len2 + 1];

            for (int i = 0; i <= len1; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= len2; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost
                    );
                }
            }

            return matrix[len1, len2];
        }

        /// <summary>
        /// Extracts a 4-digit year from the start of a date string (e.g. "1997-08-12" -> 1997).
        /// Returns 0 when no year can be parsed.
        /// </summary>
        public uint ExtractYearFromDate(string? dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString) || dateString.Length < 4)
                return 0;

            var yearString = dateString.Substring(0, 4);
            return uint.TryParse(yearString, out uint year) ? year : 0;
        }
    }
}
