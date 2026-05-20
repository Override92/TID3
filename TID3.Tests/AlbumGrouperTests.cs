using System.Collections.Generic;
using System.Linq;
using TID3.Models;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    public class AlbumGrouperTests
    {
        private readonly AlbumGrouper _grouper = new();

        private static AudioFileInfo File(string album, string albumArtist, uint year = 0,
            uint track = 0, string genre = "")
            => new()
            {
                Album = album,
                AlbumArtist = albumArtist,
                Year = year,
                Track = track,
                Genre = genre
            };

        [Fact]
        public void GroupByAlbum_EmptyInput_ReturnsEmptyList()
        {
            Assert.Empty(_grouper.GroupByAlbum(new List<AudioFileInfo>()));
        }

        [Fact]
        public void GroupByAlbum_GroupsFilesSharingAnAlbum()
        {
            var files = new List<AudioFileInfo>
            {
                File("Discovery", "Daft Punk"),
                File("Discovery", "Daft Punk"),
                File("Homework", "Daft Punk")
            };

            var groups = _grouper.GroupByAlbum(files);

            Assert.Equal(2, groups.Count);
            Assert.Equal(2, groups.Single(g => g.Album == "Discovery").Tracks.Count);
            Assert.Single(groups.Single(g => g.Album == "Homework").Tracks);
        }

        [Fact]
        public void GroupByAlbum_OrdersTracksByTrackNumber()
        {
            var files = new List<AudioFileInfo>
            {
                File("A", "Artist", track: 3),
                File("A", "Artist", track: 1),
                File("A", "Artist", track: 2)
            };

            var group = Assert.Single(_grouper.GroupByAlbum(files));

            Assert.Equal(new uint[] { 1, 2, 3 }, group.Tracks.Select(t => t.Track));
        }

        [Fact]
        public void GroupByAlbum_OrdersGroupsByArtistThenYearThenAlbum()
        {
            // Three distinct albums so each becomes its own group.
            var files = new List<AudioFileInfo>
            {
                File("Zebra", "Beta", year: 2000),
                File("Apple Two", "Alpha", year: 2010),
                File("Apple One", "Alpha", year: 1999)
            };

            var groups = _grouper.GroupByAlbum(files);

            // Alpha sorts before Beta; within Alpha, 1999 before 2010.
            Assert.Equal(new[] { "Apple One", "Apple Two", "Zebra" }, groups.Select(g => g.Album));
            Assert.Equal(new[] { "Alpha", "Alpha", "Beta" }, groups.Select(g => g.Artist));
            Assert.Equal(new uint[] { 1999, 2010, 2000 }, groups.Select(g => g.Year));
        }

        [Fact]
        public void GroupByAlbum_YearIsTheMaxYearInTheGroup()
        {
            var files = new List<AudioFileInfo>
            {
                File("A", "Artist", year: 1999),
                File("A", "Artist", year: 2005),
                File("A", "Artist", year: 2001)
            };

            var group = Assert.Single(_grouper.GroupByAlbum(files));

            Assert.Equal(2005u, group.Year);
        }

        [Fact]
        public void GroupByAlbum_Artist_PrefersAlbumArtist()
        {
            var files = new List<AudioFileInfo>
            {
                new() { Album = "A", AlbumArtist = "Album Artist", Artist = "Track Artist" }
            };

            var group = Assert.Single(_grouper.GroupByAlbum(files));

            Assert.Equal("Album Artist", group.Artist);
        }

        [Fact]
        public void GroupByAlbum_Artist_FallsBackToTrackArtist_WhenAlbumArtistBlank()
        {
            var files = new List<AudioFileInfo>
            {
                new() { Album = "A", AlbumArtist = "", Artist = "Track Artist" }
            };

            var group = Assert.Single(_grouper.GroupByAlbum(files));

            Assert.Equal("Track Artist", group.Artist);
        }

        [Fact]
        public void GroupByAlbum_Artist_UnknownArtist_WhenNoArtistInfo()
        {
            var files = new List<AudioFileInfo> { new() { Album = "A" } };

            var group = Assert.Single(_grouper.GroupByAlbum(files));

            Assert.Equal("Unknown Artist", group.Artist);
        }

        [Fact]
        public void GroupByAlbum_GenreComesFromFirstFileInGroup()
        {
            var files = new List<AudioFileInfo>
            {
                File("A", "Artist", track: 1, genre: "Electronic"),
                File("A", "Artist", track: 2, genre: "House")
            };

            var group = Assert.Single(_grouper.GroupByAlbum(files));

            Assert.Equal("Electronic", group.Genre);
        }
    }
}
