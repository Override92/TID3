// CoverArtFileResolver.cs - Pure resolution of album-cover image files in a folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TID3.Services
{
    /// <summary>
    /// Chooses which image file in an album folder is the cover, and which filename
    /// a cover should be saved as. Operates purely on lists of filenames (no
    /// filesystem access), so it can be unit tested directly.
    /// </summary>
    public static class CoverArtFileResolver
    {
        /// <summary>Standard cover filenames, in preference order.</summary>
        public static readonly IReadOnlyList<string> StandardCoverNames = new[]
        {
            "folder.jpg", "folder.jpeg", "folder.png",
            "cover.jpg",  "cover.jpeg",  "cover.png",
            "front.jpg",  "front.jpeg",  "front.png",
            "albumart.jpg", "albumart.jpeg", "albumart.png",
            "album.jpg",  "album.jpeg",  "album.png"
        };

        /// <summary>Recognized image file extensions (lower-case, with leading dot).</summary>
        public static readonly IReadOnlyList<string> ImageExtensions = new[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif"
        };

        // Substrings that mark an image as album art when no standard name is present.
        private static readonly string[] AlbumKeywords = { "cover", "front", "album", "folder" };

        // Preference order when choosing a filename to save a new cover as
        // (jpg variants first, then jpeg, then png).
        private static readonly string[] SaveNamePreference =
        {
            "folder.jpg", "cover.jpg", "front.jpg", "albumart.jpg", "album.jpg",
            "folder.jpeg", "cover.jpeg", "front.jpeg", "albumart.jpeg", "album.jpeg",
            "folder.png", "cover.png", "front.png", "albumart.png", "album.png"
        };

        /// <summary>True when the filename has a recognized image extension.</summary>
        public static bool IsImageFile(string fileName)
            => !string.IsNullOrEmpty(fileName)
               && ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

        /// <summary>
        /// Returns candidate cover filenames in priority order:
        /// 1) standard cover names present in the folder (in preference order),
        /// 2) other image files whose name contains an album keyword (alphabetical),
        /// 3) any remaining image files (alphabetical).
        /// Matching is case-insensitive; the returned values preserve the folder's
        /// actual filename casing. Accepts full paths or bare filenames.
        /// </summary>
        public static List<string> ResolveCoverCandidates(IEnumerable<string> folderFiles)
        {
            var names = folderFiles
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

            var candidates = new List<string>();

            // 1) Standard names, in preference order.
            foreach (var standard in StandardCoverNames)
            {
                var match = names.FirstOrDefault(n => string.Equals(n, standard, StringComparison.OrdinalIgnoreCase));
                if (match != null && !candidates.Contains(match))
                    candidates.Add(match);
            }

            var imageFiles = names
                .Where(IsImageFile)
                .OrderBy(n => n.ToLowerInvariant(), StringComparer.Ordinal)
                .ToList();

            // 2) Keyword-bearing images not already chosen.
            foreach (var img in imageFiles)
            {
                if (candidates.Contains(img)) continue;
                var stem = Path.GetFileNameWithoutExtension(img).ToLowerInvariant();
                if (AlbumKeywords.Any(stem.Contains))
                    candidates.Add(img);
            }

            // 3) Any remaining images.
            foreach (var img in imageFiles)
            {
                if (!candidates.Contains(img))
                    candidates.Add(img);
            }

            return candidates;
        }

        /// <summary>The single best cover filename in the folder, or null if none.</summary>
        public static string? ResolveCoverFileName(IEnumerable<string> folderFiles)
            => ResolveCoverCandidates(folderFiles).FirstOrDefault();

        /// <summary>
        /// The filename a cover should be written as: reuse an existing standard
        /// cover file (so it is replaced in place), otherwise default to "cover.jpg".
        /// </summary>
        public static string ResolveSaveFileName(IEnumerable<string> folderFiles)
        {
            var names = folderFiles
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

            foreach (var preferred in SaveNamePreference)
            {
                var match = names.FirstOrDefault(n => string.Equals(n, preferred, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return "cover.jpg";
        }
    }
}
