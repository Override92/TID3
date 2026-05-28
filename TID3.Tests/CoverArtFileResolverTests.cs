using System.Collections.Generic;
using System.Linq;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    public class CoverArtFileResolverTests
    {
        // ---- IsImageFile --------------------------------------------------------

        [Theory]
        [InlineData("cover.jpg", true)]
        [InlineData("CACHE.PNG", true)]      // case-insensitive
        [InlineData("art.jpeg", true)]
        [InlineData("scan.bmp", true)]
        [InlineData("anim.gif", true)]
        [InlineData("notes.txt", false)]
        [InlineData("song.mp3", false)]
        [InlineData("noextension", false)]
        public void IsImageFile_Cases(string fileName, bool expected)
        {
            Assert.Equal(expected, CoverArtFileResolver.IsImageFile(fileName));
        }

        // ---- ResolveCoverFileName: standard names -------------------------------

        [Fact]
        public void Resolve_PrefersStandardNameOverOtherImage()
        {
            var files = new[] { "random.jpg", "cover.jpg" };

            Assert.Equal("cover.jpg", CoverArtFileResolver.ResolveCoverFileName(files));
        }

        [Fact]
        public void Resolve_FollowsStandardNamePreferenceOrder()
        {
            // "folder.*" outranks "cover.*" which outranks "album.*".
            var files = new[] { "album.png", "cover.jpg", "folder.jpg" };

            Assert.Equal("folder.jpg", CoverArtFileResolver.ResolveCoverFileName(files));
        }

        [Fact]
        public void Resolve_StandardNameMatchIsCaseInsensitive_PreservesActualCasing()
        {
            var files = new[] { "Folder.JPG" };

            Assert.Equal("Folder.JPG", CoverArtFileResolver.ResolveCoverFileName(files));
        }

        [Fact]
        public void Resolve_AcceptsFullPaths_ReturnsBareFilename()
        {
            var files = new[]
            {
                @"C:\Music\Artist\Album\track01.mp3",
                @"C:\Music\Artist\Album\cover.jpg"
            };

            Assert.Equal("cover.jpg", CoverArtFileResolver.ResolveCoverFileName(files));
        }

        // ---- ResolveCoverFileName: keyword + fallback ---------------------------

        [Fact]
        public void Resolve_KeywordImage_WhenNoStandardName()
        {
            // "albumcover" contains the "album"/"cover" keyword; "dsc_0001" does not.
            var files = new[] { "dsc_0001.jpg", "albumcover.png" };

            Assert.Equal("albumcover.png", CoverArtFileResolver.ResolveCoverFileName(files));
        }

        [Fact]
        public void Resolve_FirstAlphabeticalImage_WhenNoStandardOrKeyword()
        {
            var files = new[] { "zoo.png", "abba.jpg", "middle.jpeg" };

            Assert.Equal("abba.jpg", CoverArtFileResolver.ResolveCoverFileName(files));
        }

        [Fact]
        public void Resolve_NullWhenNoImages()
        {
            var files = new[] { "track01.mp3", "notes.txt", "playlist.m3u" };

            Assert.Null(CoverArtFileResolver.ResolveCoverFileName(files));
        }

        [Fact]
        public void Resolve_NullForEmptyFolder()
        {
            Assert.Null(CoverArtFileResolver.ResolveCoverFileName(new List<string>()));
        }

        // ---- ResolveCoverCandidates: ordering -----------------------------------

        [Fact]
        public void Candidates_OrderStandardThenKeywordThenRemaining()
        {
            var files = new[] { "zoo.png", "front_alt.jpg", "cover.jpg", "aaa.gif" };

            var candidates = CoverArtFileResolver.ResolveCoverCandidates(files);

            // cover.jpg (standard) first; then "front_alt" (keyword "front");
            // then the remaining images alphabetically (aaa.gif, zoo.png).
            Assert.Equal(new[] { "cover.jpg", "front_alt.jpg", "aaa.gif", "zoo.png" }, candidates);
        }

        [Fact]
        public void Candidates_ExcludesNonImageFiles()
        {
            var files = new[] { "cover.jpg", "track.mp3", "notes.txt" };

            var candidates = CoverArtFileResolver.ResolveCoverCandidates(files);

            Assert.Equal(new[] { "cover.jpg" }, candidates);
        }

        [Fact]
        public void Candidates_NoDuplicates_WhenStandardNameAlsoHasKeyword()
        {
            // "cover.jpg" is a standard name AND contains the "cover" keyword;
            // it must appear exactly once.
            var files = new[] { "cover.jpg", "other.png" };

            var candidates = CoverArtFileResolver.ResolveCoverCandidates(files);

            Assert.Single(candidates.Where(c => c == "cover.jpg"));
        }

        // ---- ResolveSaveFileName ------------------------------------------------

        [Fact]
        public void SaveFileName_ReusesExistingStandardFile()
        {
            var files = new[] { "front.png", "track.mp3" };

            Assert.Equal("front.png", CoverArtFileResolver.ResolveSaveFileName(files));
        }

        [Fact]
        public void SaveFileName_PrefersJpgOverPng_WhenBothExist()
        {
            // Save preference puts all .jpg variants ahead of .png variants.
            var files = new[] { "album.png", "cover.jpg" };

            Assert.Equal("cover.jpg", CoverArtFileResolver.ResolveSaveFileName(files));
        }

        [Fact]
        public void SaveFileName_DefaultsToCoverJpg_WhenNoExistingCover()
        {
            var files = new[] { "track01.mp3", "random.gif" };

            Assert.Equal("cover.jpg", CoverArtFileResolver.ResolveSaveFileName(files));
        }

        [Fact]
        public void SaveFileName_DefaultsToCoverJpg_ForEmptyFolder()
        {
            Assert.Equal("cover.jpg", CoverArtFileResolver.ResolveSaveFileName(new List<string>()));
        }
    }
}
