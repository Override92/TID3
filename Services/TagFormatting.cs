// TagFormatting.cs - Pure helpers for converting between tag values and display strings.
using System;
using System.Linq;

namespace TID3.Services
{
    /// <summary>
    /// Pure conversions between TagLib multi-value fields / audio properties and the
    /// display strings shown in the editor. No I/O, so directly unit testable.
    /// </summary>
    public static class TagFormatting
    {
        /// <summary>
        /// Joins a multi-value tag field (e.g. performers, genres) into a single
        /// comma-separated string. Null/empty arrays become "".
        /// </summary>
        public static string JoinValues(string[]? values)
        {
            if (values == null || values.Length == 0)
                return string.Empty;

            if (values.Length == 1)
                return values[0] ?? string.Empty;

            return string.Join(", ", values);
        }

        /// <summary>
        /// Splits a comma-separated editor string back into a multi-value tag field,
        /// trimming whitespace around each entry. Inverse of <see cref="JoinValues"/>.
        /// </summary>
        public static string[] SplitValues(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return Array.Empty<string>();

            return value.Split(',').Select(s => s.Trim()).ToArray();
        }

        /// <summary>Formats a track duration as mm:ss.</summary>
        public static string FormatDuration(TimeSpan duration)
            => duration.ToString(@"mm\:ss");

        /// <summary>Formats an audio bitrate, or "Unknown" when not available.</summary>
        public static string FormatBitrate(int bitrate)
            => bitrate > 0 ? $"{bitrate} kbps" : "Unknown";

        /// <summary>
        /// Formats a byte count as a human-readable size (B/KB/MB/GB/TB), e.g. "4.2 MB".
        /// Uses the current culture's number formatting.
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int counter = 0;
            decimal number = bytes < 0 ? 0 : bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }
    }
}
