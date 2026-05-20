using System.Text.Json;
using TID3.Models;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    /// <summary>
    /// Tests built from real-world quirks: actual Discogs search-result shapes,
    /// real band/album naming conventions, compilations, reissues, etc.
    /// Each test documents how the code behaves on data that actually occurs.
    /// </summary>
    public class RealWorldDataTests
    {
        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

        private readonly MatchScoringService _scorer = new();
        private readonly AlbumGrouper _grouper = new();

        // =====================================================================
        //  Discogs search-result quirks
        // =====================================================================

        [Fact]
        public void Discogs_RealSearchResult_RickAstleySingle()
        {
            // Shape of an actual Discogs database/search result for a 7" single.
            var release = DiscogsResponseParser.ParseRelease(Json(@"{
                ""id"": 249504,
                ""title"": ""Rick Astley - Never Gonna Give You Up"",
                ""year"": ""1987"",
                ""format"": [""Vinyl"", ""7\"""", ""45 RPM"", ""Single""],
                ""genre"": [""Electronic"", ""Pop""],
                ""style"": [""Synth-pop""],
                ""cover_image"": ""https://i.discogs.com/x/R-249504.jpeg"",
                ""thumb"": ""https://i.discogs.com/s/R-249504.jpeg""
            }"));

            Assert.Equal(249504, release.Id);
            Assert.Equal("Rick Astley", release.Artist);
            Assert.Equal("Never Gonna Give You Up", release.Title);
            Assert.Equal("1987", release.Year);
            Assert.Equal("Electronic, Pop", release.Genre);
            Assert.Equal(0, release.TrackCount);          // a single — no tracklist in search results
            Assert.Equal("https://i.discogs.com/x/R-249504.jpeg", release.CoverArtUrl);
        }

        [Fact]
        public void Discogs_ArtistDisambiguationNumber_IsStripped()
        {
            // Discogs disambiguates same-named artists with "(2)", "(3)" suffixes.
            // The grunge Nirvana is "Nirvana (2)" because a 1960s band holds "Nirvana".
            var release = DiscogsResponseParser.ParseRelease(
                Json(@"{ ""id"": 367153, ""title"": ""Nirvana (2) - Nevermind"" }"));

            Assert.Equal("Nirvana", release.Artist);       // disambiguation index stripped
            Assert.Equal("Nevermind", release.Title);
        }

        [Fact]
        public void Discogs_MultipleArtists_EachDisambiguationStripped()
        {
            // basic_information.artists carries the "(N)" suffix on each name too.
            var release = DiscogsResponseParser.ParseRelease(Json(@"{
                ""id"": 8, ""title"": ""ignored"",
                ""basic_information"": { ""artists"": [
                    { ""name"": ""Nirvana (2)"" }, { ""name"": ""Pulp (3)"" } ] }
            }"));

            Assert.Equal("Nirvana, Pulp", release.Artist);
        }

        [Fact]
        public void Discogs_RealAlbumNameEndingInParenNumber_IsUntouched()
        {
            // The disambiguation strip only removes a trailing "(digits)" from the
            // artist; album titles (the part after " - ") are never touched.
            var release = DiscogsResponseParser.ParseRelease(
                Json(@"{ ""id"": 9, ""title"": ""Wu-Tang Clan - Enter The Wu-Tang (36 Chambers)"" }"));

            Assert.Equal("Wu-Tang Clan", release.Artist);
            Assert.Equal("Enter The Wu-Tang (36 Chambers)", release.Title);
        }

        [Fact]
        public void Discogs_VariousArtistsCompilation()
        {
            var release = DiscogsResponseParser.ParseRelease(
                Json(@"{ ""id"": 1, ""title"": ""Various - Trainspotting (Music From The Motion Picture)"" }"));

            Assert.Equal("Various", release.Artist);
            Assert.Equal("Trainspotting (Music From The Motion Picture)", release.Title);
        }

        [Fact]
        public void Discogs_HyphenatedBandName_DoesNotBreakArtistAlbumSplit()
        {
            // "Wu-Tang" contains a hyphen but no " - " separator, so the split is clean.
            var release = DiscogsResponseParser.ParseRelease(
                Json(@"{ ""id"": 2, ""title"": ""Wu-Tang Clan - Enter The Wu-Tang (36 Chambers)"" }"));

            Assert.Equal("Wu-Tang Clan", release.Artist);
            Assert.Equal("Enter The Wu-Tang (36 Chambers)", release.Title);
        }

        [Fact]
        public void Discogs_AmpersandBandName_Survives()
        {
            var release = DiscogsResponseParser.ParseRelease(
                Json(@"{ ""id"": 3, ""title"": ""Simon & Garfunkel - Bridge Over Troubled Water"" }"));

            Assert.Equal("Simon & Garfunkel", release.Artist);
            Assert.Equal("Bridge Over Troubled Water", release.Title);
        }

        [Fact]
        public void Discogs_MissingYear_IsEmptyString()
        {
            // Many Discogs search results omit "year" entirely.
            var release = DiscogsResponseParser.ParseRelease(
                Json(@"{ ""id"": 4, ""title"": ""Boards Of Canada - Music Has The Right To Children"" }"));

            Assert.Equal("", release.Year);
        }

        [Fact]
        public void Discogs_EmptyStringYear_StaysEmpty()
        {
            var release = DiscogsResponseParser.ParseRelease(
                Json(@"{ ""id"": 5, ""title"": ""Artist - Album"", ""year"": """" }"));

            Assert.Equal("", release.Year);
        }

        [Fact]
        public void Discogs_NoCoverImage_FallsBackToThumb()
        {
            var release = DiscogsResponseParser.ParseRelease(Json(@"{
                ""id"": 6, ""title"": ""Artist - Album"",
                ""cover_image"": """",
                ""thumb"": ""https://i.discogs.com/s/spacer.gif""
            }"));

            Assert.Equal("https://i.discogs.com/s/spacer.gif", release.CoverArtUrl);
        }

        [Fact]
        public void Discogs_EmptySearch_ReturnsNoReleases()
        {
            Assert.Empty(DiscogsResponseParser.ParseSearchResults(Json(@"{ ""results"": [] }")));
        }

        [Fact]
        public void Discogs_TrackCountFromBoxSetFormatDescription()
        {
            // Box sets sometimes spell the track total out in a format description.
            var release = DiscogsResponseParser.ParseRelease(Json(@"{
                ""id"": 7, ""title"": ""Artist - Anthology"",
                ""basic_information"": { ""formats"": [
                    { ""descriptions"": [""Compilation"", ""Remastered"", ""63 tracks""] } ] }
            }"));

            Assert.Equal(63, release.TrackCount);
        }

        // =====================================================================
        //  String similarity on real artist/album naming conventions
        // =====================================================================

        [Fact]
        public void Similarity_AmpersandVsAnd_AreTreatedEqual()
        {
            // "&" is normalized to "and".
            Assert.Equal(1.0, _scorer.CalculateStringSimilarity("Simon & Garfunkel", "Simon and Garfunkel"));
        }

        [Fact]
        public void Similarity_ApostropheIsIgnored()
        {
            // "Guns N' Roses" vs "Guns N Roses" — the apostrophe is stripped.
            Assert.Equal(1.0, _scorer.CalculateStringSimilarity("Guns N' Roses", "Guns N Roses"));
        }

        [Fact]
        public void Similarity_SlashInBandName_IsCaseInsensitiveMatch()
        {
            Assert.Equal(1.0, _scorer.CalculateStringSimilarity("AC/DC", "ac/dc"));
        }

        [Fact]
        public void Similarity_TheePrefix_ScoresAsContainsMatch()
        {
            // "The Beatles" vs "Beatles" — one contains the other.
            Assert.Equal(0.8, _scorer.CalculateStringSimilarity("The Beatles", "Beatles"));
        }

        [Fact]
        public void Similarity_RemasterSuffix_ScoresAsContainsMatch()
        {
            // Streaming/MB titles often carry a "(Remastered)" suffix.
            Assert.Equal(0.8, _scorer.CalculateStringSimilarity("Abbey Road (Remastered)", "Abbey Road"));
        }

        [Fact]
        public void Similarity_FeaturedArtistSuffix_ScoresAsContainsMatch()
        {
            Assert.Equal(0.8, _scorer.CalculateStringSimilarity("Daft Punk", "Daft Punk Feat. Pharrell Williams"));
        }

        [Fact]
        public void Similarity_Diacritics_AreNormalized_ExactMatch()
        {
            // "Sigur Rós" vs "Sigur Ros" — diacritics are folded before comparing,
            // so the accented and plain spellings match exactly.
            Assert.Equal(1.0, _scorer.CalculateStringSimilarity("Sigur Rós", "Sigur Ros"));
        }

        [Theory]
        [InlineData("Motörhead", "Motorhead")]
        [InlineData("Beyoncé", "Beyonce")]
        [InlineData("Mötley Crüe", "Motley Crue")]
        [InlineData("Céline Dion", "Celine Dion")]
        public void Similarity_DiacriticsOnVariousLetters_FoldToAscii(string accented, string plain)
        {
            Assert.Equal(1.0, _scorer.CalculateStringSimilarity(accented, plain));
        }

        // =====================================================================
        //  Match scoring on realistic releases
        // =====================================================================

        [Fact]
        public void MatchScore_CorrectAlbum_RanksAboveWrongAlbum()
        {
            // A track tagged as belonging to Radiohead's "OK Computer" (1997, 12 tracks).
            var track = new AudioFileInfo
            {
                Artist = "Radiohead",
                Album = "OK Computer",
                Title = "Paranoid Android",
                Year = 1997
            };

            var correct = new MusicBrainzRelease
            {
                Artist = "Radiohead", Title = "OK Computer", Date = "1997-05-21", TrackCount = 12
            };
            var wrong = new MusicBrainzRelease
            {
                Artist = "Radiohead", Title = "Kid A", Date = "2000-10-02", TrackCount = 11
            };

            var correctScore = _scorer.CalculateMatchScore(track, correct, loadedTrackCount: 12);
            var wrongScore = _scorer.CalculateMatchScore(track, wrong, loadedTrackCount: 12);

            Assert.True(correctScore > 0.9, $"Correct release should score high, was {correctScore}.");
            Assert.True(correctScore > wrongScore,
                $"Correct ({correctScore}) should beat wrong album ({wrongScore}).");
        }

        [Fact]
        public void MatchScore_ReissueWithExtraTitleText_StillScoresWell()
        {
            // MusicBrainz often lists deluxe reissues with extra title text.
            var track = new AudioFileInfo
            {
                Artist = "Radiohead", Album = "OK Computer", Title = "Airbag", Year = 1997
            };
            var reissue = new MusicBrainzRelease
            {
                Artist = "Radiohead",
                Title = "OK Computer OKNOTOK 1997 2017",
                Date = "2017-06-23",
                TrackCount = 23
            };

            // Album still matches as a "contains" hit; year/track-count differ.
            var score = _scorer.CalculateMatchScore(track, reissue, loadedTrackCount: 12);

            Assert.InRange(score, 0.5, 0.85);
        }

        // =====================================================================
        //  Album grouping for compilations
        // =====================================================================

        [Fact]
        public void AlbumGrouper_VariousArtistsCompilation_GroupsUnderAlbumArtist()
        {
            // A soundtrack: every track a different artist, one shared album artist.
            var files = new[]
            {
                new AudioFileInfo { Album = "Trainspotting", AlbumArtist = "Various", Artist = "Iggy Pop", Track = 1 },
                new AudioFileInfo { Album = "Trainspotting", AlbumArtist = "Various", Artist = "Underworld", Track = 2 },
                new AudioFileInfo { Album = "Trainspotting", AlbumArtist = "Various", Artist = "Blur", Track = 3 }
            };

            var group = Assert.Single(_grouper.GroupByAlbum(files));

            Assert.Equal("Trainspotting", group.Album);
            Assert.Equal("Various", group.Artist);             // album artist wins over per-track artists
            Assert.Equal(3, group.Tracks.Count);
        }

        [Fact]
        public void AlbumGrouper_MultiDiscAlbum_KeepsAllTracksInOneGroup()
        {
            // A 2-CD album: TagLib track numbers restart per disc, so two tracks
            // share Track == 1. They still belong to one album group.
            var files = new[]
            {
                new AudioFileInfo { Album = "Mellon Collie", AlbumArtist = "The Smashing Pumpkins", Track = 1 },
                new AudioFileInfo { Album = "Mellon Collie", AlbumArtist = "The Smashing Pumpkins", Track = 2 },
                new AudioFileInfo { Album = "Mellon Collie", AlbumArtist = "The Smashing Pumpkins", Track = 1 }
            };

            var group = Assert.Single(_grouper.GroupByAlbum(files));

            Assert.Equal(3, group.Tracks.Count);
        }
    }
}
