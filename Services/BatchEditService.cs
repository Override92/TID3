// BatchEditService.cs - Applies batch tag edits to a set of audio files.
using System.Collections.Generic;
using TID3.Models;

namespace TID3.Services
{
    /// <summary>
    /// A typed description of a batch edit. A null field means "leave unchanged";
    /// a non-null field means "set this value on every selected file".
    /// </summary>
    public sealed class BatchEditChanges
    {
        public string? Album { get; set; }
        public string? AlbumArtist { get; set; }
        public string? Genre { get; set; }
        public uint? Year { get; set; }

        /// <summary>Renumber Track to 1..N in list order.</summary>
        public bool AutoNumberTracks { get; set; }

        /// <summary>Normalize whitespace-only tag fields to empty strings.</summary>
        public bool CleanupTags { get; set; }

        /// <summary>Persist the album cover during save (handled by the caller).</summary>
        public bool UpdateAlbumCover { get; set; }

        /// <summary>True when at least one change is requested.</summary>
        public bool HasAny =>
            Album != null || AlbumArtist != null || Genre != null || Year != null
            || AutoNumberTracks || CleanupTags || UpdateAlbumCover;

        /// <summary>True when the only requested change is the album cover.</summary>
        public bool OnlyUpdatesAlbumCover =>
            UpdateAlbumCover && Album == null && AlbumArtist == null && Genre == null
            && Year == null && !AutoNumberTracks && !CleanupTags;
    }

    /// <summary>
    /// Applies <see cref="BatchEditChanges"/> to the in-memory tag fields of audio
    /// files. Pure (no file I/O), so it can be unit tested directly. Saving the
    /// mutated files to disk remains the caller's responsibility.
    /// </summary>
    public class BatchEditService
    {
        /// <summary>
        /// Applies the field changes to every file. Files are mutated in place;
        /// <see cref="BatchEditChanges.AutoNumberTracks"/> numbers them by list order.
        /// </summary>
        public void ApplyFieldChanges(IReadOnlyList<AudioFileInfo> files, BatchEditChanges changes)
        {
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];

                if (changes.Album != null) file.Album = changes.Album;
                if (changes.AlbumArtist != null) file.AlbumArtist = changes.AlbumArtist;
                if (changes.Genre != null) file.Genre = changes.Genre;
                if (changes.Year != null) file.Year = changes.Year.Value;

                if (changes.AutoNumberTracks) file.Track = (uint)(i + 1);

                if (changes.CleanupTags) CleanupTags(file);
            }
        }

        private static void CleanupTags(AudioFileInfo file)
        {
            if (string.IsNullOrWhiteSpace(file.Title)) file.Title = "";
            if (string.IsNullOrWhiteSpace(file.Artist)) file.Artist = "";
            if (string.IsNullOrWhiteSpace(file.Album)) file.Album = "";
            if (string.IsNullOrWhiteSpace(file.Genre)) file.Genre = "";
            if (string.IsNullOrWhiteSpace(file.AlbumArtist)) file.AlbumArtist = "";
            if (string.IsNullOrWhiteSpace(file.Comment)) file.Comment = "";
        }
    }
}
