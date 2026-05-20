// AlbumGrouper.cs - Groups a flat list of audio files into the album hierarchy.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TID3.Models;

namespace TID3.Services
{
    /// <summary>
    /// Builds the album/track hierarchy shown in the library tree from a flat
    /// list of audio files. Pure (no UI), so it can be unit tested directly.
    /// </summary>
    public class AlbumGrouper
    {
        /// <summary>
        /// Groups files by album. Within a group, tracks are ordered by track
        /// number; groups are ordered by artist, then year, then album.
        /// </summary>
        public List<AlbumGroup> GroupByAlbum(IEnumerable<AudioFileInfo> files)
        {
            return files
                .GroupBy(file => file.Album ?? "Unknown Album")
                .Select(group =>
                {
                    var first = group.First();
                    return new AlbumGroup
                    {
                        Album = group.Key,
                        Artist = ResolveArtist(first),
                        Year = group.Max(f => f.Year),
                        Genre = first.Genre ?? "",
                        Tracks = new ObservableCollection<AudioFileInfo>(group.OrderBy(f => f.Track))
                    };
                })
                .OrderBy(g => g.Artist)
                .ThenBy(g => g.Year)
                .ThenBy(g => g.Album)
                .ToList();
        }

        /// <summary>
        /// Picks the group's artist: the album artist if set, otherwise the track
        /// artist, otherwise "Unknown Artist". (Plain <c>??</c> would not work here
        /// because the tag fields are empty strings, not null.)
        /// </summary>
        private static string ResolveArtist(AudioFileInfo file)
        {
            if (!string.IsNullOrWhiteSpace(file.AlbumArtist)) return file.AlbumArtist;
            if (!string.IsNullOrWhiteSpace(file.Artist)) return file.Artist;
            return "Unknown Artist";
        }
    }
}
