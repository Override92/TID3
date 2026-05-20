using System.Collections.Generic;
using TID3.Models;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    public class BatchEditServiceTests
    {
        private readonly BatchEditService _service = new();

        private static List<AudioFileInfo> Files(int count)
        {
            var list = new List<AudioFileInfo>();
            for (int i = 0; i < count; i++)
                list.Add(new AudioFileInfo { Title = $"Track {i + 1}" });
            return list;
        }

        // ---- ApplyFieldChanges --------------------------------------------------

        [Fact]
        public void ApplyFieldChanges_SetsRequestedFields_OnEveryFile()
        {
            var files = Files(3);
            var changes = new BatchEditChanges
            {
                Album = "Discovery",
                AlbumArtist = "Daft Punk",
                Genre = "House",
                Year = 2001
            };

            _service.ApplyFieldChanges(files, changes);

            Assert.All(files, f =>
            {
                Assert.Equal("Discovery", f.Album);
                Assert.Equal("Daft Punk", f.AlbumArtist);
                Assert.Equal("House", f.Genre);
                Assert.Equal(2001u, f.Year);
            });
        }

        [Fact]
        public void ApplyFieldChanges_NullFields_AreLeftUnchanged()
        {
            var files = new List<AudioFileInfo>
            {
                new() { Album = "Original", Genre = "Rock", Year = 1999 }
            };
            // Only Album is being changed.
            var changes = new BatchEditChanges { Album = "New" };

            _service.ApplyFieldChanges(files, changes);

            Assert.Equal("New", files[0].Album);
            Assert.Equal("Rock", files[0].Genre);   // untouched
            Assert.Equal(1999u, files[0].Year);     // untouched
        }

        [Fact]
        public void ApplyFieldChanges_AutoNumber_NumbersByListOrder()
        {
            var files = Files(4);

            _service.ApplyFieldChanges(files, new BatchEditChanges { AutoNumberTracks = true });

            Assert.Equal(1u, files[0].Track);
            Assert.Equal(2u, files[1].Track);
            Assert.Equal(3u, files[2].Track);
            Assert.Equal(4u, files[3].Track);
        }

        [Fact]
        public void ApplyFieldChanges_NoAutoNumber_LeavesTrackUntouched()
        {
            var files = new List<AudioFileInfo> { new() { Track = 7 } };

            _service.ApplyFieldChanges(files, new BatchEditChanges { Genre = "Jazz" });

            Assert.Equal(7u, files[0].Track);
        }

        [Fact]
        public void ApplyFieldChanges_Cleanup_NormalizesWhitespaceOnlyFields()
        {
            var files = new List<AudioFileInfo>
            {
                new() { Title = "Real Title", Artist = "   ", Genre = "  ", Comment = "keep me" }
            };

            _service.ApplyFieldChanges(files, new BatchEditChanges { CleanupTags = true });

            Assert.Equal("Real Title", files[0].Title); // non-empty preserved
            Assert.Equal("", files[0].Artist);          // whitespace -> empty
            Assert.Equal("", files[0].Genre);           // whitespace -> empty
            Assert.Equal("keep me", files[0].Comment);  // non-empty preserved
        }

        [Fact]
        public void ApplyFieldChanges_EmptyChanges_IsNoOp()
        {
            var files = new List<AudioFileInfo>
            {
                new() { Album = "A", Genre = "G", Year = 2000, Track = 5 }
            };

            _service.ApplyFieldChanges(files, new BatchEditChanges());

            Assert.Equal("A", files[0].Album);
            Assert.Equal("G", files[0].Genre);
            Assert.Equal(2000u, files[0].Year);
            Assert.Equal(5u, files[0].Track);
        }

        [Fact]
        public void ApplyFieldChanges_EmptyFileList_DoesNotThrow()
        {
            _service.ApplyFieldChanges(new List<AudioFileInfo>(),
                new BatchEditChanges { Album = "X", AutoNumberTracks = true });
        }

        // ---- BatchEditChanges flags --------------------------------------------

        [Fact]
        public void HasAny_FalseForEmptyChanges()
        {
            Assert.False(new BatchEditChanges().HasAny);
        }

        [Theory]
        [InlineData("album")]
        [InlineData("year")]
        [InlineData("autonumber")]
        [InlineData("cleanup")]
        [InlineData("cover")]
        public void HasAny_TrueWhenAnySingleChangeSet(string which)
        {
            var c = new BatchEditChanges();
            switch (which)
            {
                case "album": c.Album = "x"; break;
                case "year": c.Year = 2000; break;
                case "autonumber": c.AutoNumberTracks = true; break;
                case "cleanup": c.CleanupTags = true; break;
                case "cover": c.UpdateAlbumCover = true; break;
            }

            Assert.True(c.HasAny);
        }

        [Fact]
        public void OnlyUpdatesAlbumCover_TrueWhenCoverIsTheSoleChange()
        {
            Assert.True(new BatchEditChanges { UpdateAlbumCover = true }.OnlyUpdatesAlbumCover);
        }

        [Fact]
        public void OnlyUpdatesAlbumCover_FalseWhenOtherChangesPresent()
        {
            Assert.False(new BatchEditChanges { UpdateAlbumCover = true, Genre = "Rock" }.OnlyUpdatesAlbumCover);
            Assert.False(new BatchEditChanges { UpdateAlbumCover = true, AutoNumberTracks = true }.OnlyUpdatesAlbumCover);
        }

        [Fact]
        public void OnlyUpdatesAlbumCover_FalseWhenCoverNotRequested()
        {
            Assert.False(new BatchEditChanges { Genre = "Rock" }.OnlyUpdatesAlbumCover);
        }
    }
}
