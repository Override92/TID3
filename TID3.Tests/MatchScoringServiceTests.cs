using TID3.Models;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    public class MatchScoringServiceTests
    {
        private readonly MatchScoringService _scorer = new();

        // ---- CalculateMatchScore (MusicBrainz) ----------------------------------

        [Fact]
        public void Score_MusicBrainz_NullFile_ReturnsZero()
        {
            var release = new MusicBrainzRelease { Artist = "Pink Floyd", Title = "The Wall" };

            Assert.Equal(0.0, _scorer.CalculateMatchScore(null, release, loadedTrackCount: 10));
        }

        [Fact]
        public void Score_MusicBrainz_PerfectMatch_ReturnsOne()
        {
            var file = new AudioFileInfo
            {
                Artist = "Pink Floyd",
                Album = "The Wall",
                Title = "The Wall",
                Year = 1979
            };
            var release = new MusicBrainzRelease
            {
                Artist = "Pink Floyd",
                Title = "The Wall",
                Date = "1979-11-30",
                TrackCount = 26
            };

            Assert.Equal(1.0, _scorer.CalculateMatchScore(file, release, loadedTrackCount: 26), precision: 6);
        }

        [Fact]
        public void Score_MusicBrainz_ZeroTrackCount_LosesTrackCountWeight()
        {
            var file = new AudioFileInfo
            {
                Artist = "Pink Floyd",
                Album = "The Wall",
                Title = "The Wall",
                Year = 1979
            };
            var release = new MusicBrainzRelease
            {
                Artist = "Pink Floyd",
                Title = "The Wall",
                Date = "1979-11-30",
                TrackCount = 0 // unknown -> the 20% track-count weight contributes nothing
            };

            // 0.35 + 0.30 + 0 + 0.10 + 0.05 = 0.80
            Assert.Equal(0.80, _scorer.CalculateMatchScore(file, release, loadedTrackCount: 26), precision: 6);
        }

        [Fact]
        public void Score_MusicBrainz_CompleteMismatch_ScoresLow()
        {
            var file = new AudioFileInfo { Artist = "Pink Floyd", Album = "The Wall", Year = 1979 };
            var release = new MusicBrainzRelease
            {
                Artist = "Wu-Tang Clan",
                Title = "Enter the Wu-Tang",
                Date = "1993-11-09",
                TrackCount = 1
            };

            var score = _scorer.CalculateMatchScore(file, release, loadedTrackCount: 26);

            Assert.True(score < 0.3, $"Expected a low score for an unrelated release, got {score}.");
        }

        [Fact]
        public void Score_MusicBrainz_CloseYear_GivesPartialCredit()
        {
            var exact = new AudioFileInfo { Artist = "A", Album = "B", Year = 2000 };
            var release2001 = new MusicBrainzRelease { Artist = "A", Title = "B", Date = "2001" };
            var release2010 = new MusicBrainzRelease { Artist = "A", Title = "B", Date = "2010" };

            var closeScore = _scorer.CalculateMatchScore(exact, release2001, loadedTrackCount: 0);
            var farScore = _scorer.CalculateMatchScore(exact, release2010, loadedTrackCount: 0);

            // A one-year gap earns partial year credit; a ten-year gap earns none.
            Assert.True(closeScore > farScore,
                $"Close year ({closeScore}) should outscore far year ({farScore}).");
        }

        // ---- CalculateMatchScore (Discogs) --------------------------------------

        [Fact]
        public void Score_Discogs_NullFile_ReturnsZero()
        {
            var release = new DiscogsRelease { Artist = "Pink Floyd", Title = "The Wall" };

            Assert.Equal(0.0, _scorer.CalculateMatchScore(null, release, loadedTrackCount: 10));
        }

        [Fact]
        public void Score_Discogs_PerfectMatch_ReturnsOne()
        {
            var file = new AudioFileInfo
            {
                Artist = "Daft Punk",
                Album = "Discovery",
                Title = "Discovery",
                Year = 2001
            };
            var release = new DiscogsRelease
            {
                Artist = "Daft Punk",
                Title = "Discovery",
                Year = "2001",
                TrackCount = 14
            };

            Assert.Equal(1.0, _scorer.CalculateMatchScore(file, release, loadedTrackCount: 14), precision: 6);
        }

        [Fact]
        public void Score_Discogs_UnparseableYear_LosesYearWeight()
        {
            var file = new AudioFileInfo
            {
                Artist = "Daft Punk",
                Album = "Discovery",
                Title = "Discovery",
                Year = 2001
            };
            var release = new DiscogsRelease
            {
                Artist = "Daft Punk",
                Title = "Discovery",
                Year = "n/a", // cannot be parsed -> no year credit
                TrackCount = 14
            };

            // 0.35 + 0.30 + 0.20 + 0 + 0.05 = 0.90
            Assert.Equal(0.90, _scorer.CalculateMatchScore(file, release, loadedTrackCount: 14), precision: 6);
        }

        // ---- CalculateStringSimilarity ------------------------------------------

        [Fact]
        public void StringSimilarity_ExactAfterNormalization_ReturnsOne()
        {
            Assert.Equal(1.0, _scorer.CalculateStringSimilarity("Pink Floyd", "pink floyd"));
        }

        [Fact]
        public void StringSimilarity_OneContainsOther_ReturnsPointEight()
        {
            Assert.Equal(0.8, _scorer.CalculateStringSimilarity("Beatles", "The Beatles"));
        }

        [Theory]
        [InlineData("", "anything")]
        [InlineData("anything", "")]
        public void StringSimilarity_EmptyInput_ReturnsZero(string a, string b)
        {
            Assert.Equal(0.0, _scorer.CalculateStringSimilarity(a, b));
        }

        [Fact]
        public void StringSimilarity_UnrelatedStrings_ScoresBelowContainsThreshold()
        {
            var similarity = _scorer.CalculateStringSimilarity("Metallica", "Megadeth");

            Assert.InRange(similarity, 0.0, 0.799);
        }

        // ---- CalculateLevenshteinDistance ---------------------------------------

        [Theory]
        [InlineData("", "", 0)]
        [InlineData("abc", "abc", 0)]
        [InlineData("kitten", "sitting", 3)]
        [InlineData("abc", "", 3)]
        [InlineData("flaw", "lawn", 2)]
        public void Levenshtein_KnownPairs(string a, string b, int expected)
        {
            Assert.Equal(expected, _scorer.CalculateLevenshteinDistance(a, b));
        }

        // ---- NormalizeForComparison ---------------------------------------------

        [Theory]
        [InlineData("Rock & Roll", "rock and roll")]
        [InlineData("Don't Stop", "dont stop")]
        [InlineData("Pink-Floyd", "pink floyd")]
        [InlineData("  Hello  World  ", "hello world")]
        [InlineData("ALREADY NORMAL", "already normal")]
        public void NormalizeForComparison_Cases(string input, string expected)
        {
            Assert.Equal(expected, _scorer.NormalizeForComparison(input));
        }

        // ---- ExtractYearFromDate ------------------------------------------------

        [Theory]
        [InlineData("1997-08-12", 1997u)]
        [InlineData("2020", 2020u)]
        [InlineData("2020-01", 2020u)]
        [InlineData("", 0u)]
        [InlineData(null, 0u)]
        [InlineData("199", 0u)]
        [InlineData("abcd", 0u)]
        public void ExtractYearFromDate_Cases(string? input, uint expected)
        {
            Assert.Equal(expected, _scorer.ExtractYearFromDate(input));
        }
    }
}
