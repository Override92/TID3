using System.Linq;
using System.Text.Json;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    public class MusicBrainzResponseParserTests
    {
        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

        // ---- ParseSearchResults -------------------------------------------------

        [Fact]
        public void ParseSearchResults_NoReleasesProperty_ReturnsEmpty()
        {
            Assert.Empty(MusicBrainzResponseParser.ParseSearchResults(Json("{}")));
        }

        [Fact]
        public void ParseSearchResults_MapsCoreFields()
        {
            var root = Json(@"{
                ""releases"": [
                    {
                        ""id"": ""fc92a487-3088-4f3f-9b0a-1a2c4c2f7f01"",
                        ""title"": ""OK Computer"",
                        ""date"": ""1997-05-21"",
                        ""score"": 100,
                        ""track-count"": 12,
                        ""artist-credit"": [ { ""name"": ""Radiohead"" } ]
                    }
                ]
            }");

            var release = Assert.Single(MusicBrainzResponseParser.ParseSearchResults(root));

            Assert.Equal("fc92a487-3088-4f3f-9b0a-1a2c4c2f7f01", release.Id);
            Assert.Equal("OK Computer", release.Title);
            Assert.Equal("Radiohead", release.Artist);
            Assert.Equal("1997-05-21", release.Date);
            Assert.Equal(100, release.Score);
            Assert.Equal(12, release.TrackCount);
        }

        [Fact]
        public void ParseSearchResults_MissingFields_GetSafeDefaults()
        {
            var release = Assert.Single(
                MusicBrainzResponseParser.ParseSearchResults(Json(@"{ ""releases"": [ {} ] }")));

            Assert.Equal("", release.Id);
            Assert.Equal("", release.Title);
            Assert.Equal("Unknown Artist", release.Artist);
            Assert.Equal("", release.Date);
            Assert.Equal(0, release.Score);
            Assert.Equal(0, release.TrackCount);
        }

        // ---- GetArtistFromCredit ------------------------------------------------

        [Fact]
        public void GetArtistFromCredit_ReadsFirstCreditName()
        {
            // MusicBrainz puts the credited (joined) name at the credit level.
            var el = Json(@"{ ""artist-credit"": [
                { ""name"": ""David Bowie"" }, { ""name"": ""Queen"" } ] }");

            Assert.Equal("David Bowie", MusicBrainzResponseParser.GetArtistFromCredit(el));
        }

        [Fact]
        public void GetArtistFromCredit_UnknownArtist_WhenNoCredit()
        {
            Assert.Equal("Unknown Artist", MusicBrainzResponseParser.GetArtistFromCredit(Json("{}")));
        }

        // ---- GetTrackCount ------------------------------------------------------

        [Fact]
        public void GetTrackCount_PrefersExplicitTrackCount()
        {
            Assert.Equal(11, MusicBrainzResponseParser.GetTrackCount(Json(@"{ ""track-count"": 11 }")));
        }

        [Fact]
        public void GetTrackCount_SumsMediaTrackCounts_ForMultiDiscReleases()
        {
            // A 2-CD release: sum the per-medium track-count values.
            var el = Json(@"{ ""media"": [ { ""track-count"": 14 }, { ""track-count"": 13 } ] }");

            Assert.Equal(27, MusicBrainzResponseParser.GetTrackCount(el));
        }

        [Fact]
        public void GetTrackCount_FallsBackToMediaTracksArrayLength()
        {
            var el = Json(@"{ ""media"": [ { ""tracks"": [ {}, {}, {} ] } ] }");

            Assert.Equal(3, MusicBrainzResponseParser.GetTrackCount(el));
        }

        [Fact]
        public void GetTrackCount_ZeroWhenUnavailable()
        {
            Assert.Equal(0, MusicBrainzResponseParser.GetTrackCount(Json("{}")));
        }

        // ---- ParseReleaseDetails ------------------------------------------------

        [Fact]
        public void ParseReleaseDetails_ReadsTracksFromMedia()
        {
            var root = Json(@"{
                ""id"": ""r1"",
                ""title"": ""Discovery"",
                ""date"": ""2001-03-12"",
                ""artist-credit"": [ { ""name"": ""Daft Punk"" } ],
                ""media"": [ { ""tracks"": [
                    { ""title"": ""One More Time"", ""position"": 1, ""length"": 320000 },
                    { ""title"": ""Aerodynamic"", ""position"": 2, ""length"": 212000 }
                ] } ]
            }");

            var release = MusicBrainzResponseParser.ParseReleaseDetails(root);

            Assert.Equal("Discovery", release.Title);
            Assert.Equal("Daft Punk", release.Artist);
            Assert.Equal(2, release.TrackCount);                 // derived from tracks parsed
            Assert.Equal(2, release.Tracks.Count);
            Assert.Equal("One More Time", release.Tracks[0].Title);
            Assert.Equal(1, release.Tracks[0].Position);
            Assert.Equal(320000, release.Tracks[0].Length);
        }

        [Fact]
        public void ParseReleaseDetails_TrackUsesRecordingCredit_WhenPresent()
        {
            // Compilation: a track's recording credits a different artist than the release.
            var root = Json(@"{
                ""title"": ""A Compilation"",
                ""artist-credit"": [ { ""name"": ""Various Artists"" } ],
                ""media"": [ { ""tracks"": [
                    { ""title"": ""Guest Song"", ""recording"": {
                        ""artist-credit"": [ { ""name"": ""Aphex Twin"" } ] } }
                ] } ]
            }");

            var release = MusicBrainzResponseParser.ParseReleaseDetails(root);

            Assert.Equal("Aphex Twin", release.Tracks[0].Artist);
        }

        [Fact]
        public void ParseReleaseDetails_TrackFallsBackToReleaseArtist_WhenNoRecordingCredit()
        {
            var root = Json(@"{
                ""artist-credit"": [ { ""name"": ""Radiohead"" } ],
                ""media"": [ { ""tracks"": [ { ""title"": ""Airbag"" } ] } ]
            }");

            var release = MusicBrainzResponseParser.ParseReleaseDetails(root);

            Assert.Equal("Radiohead", release.Tracks[0].Artist);
        }

        [Fact]
        public void ParseReleaseDetails_NoMedia_HasNoTracks()
        {
            var release = MusicBrainzResponseParser.ParseReleaseDetails(
                Json(@"{ ""id"": ""r2"", ""title"": ""Single"" }"));

            Assert.Empty(release.Tracks);
            Assert.Equal(0, release.TrackCount);
        }
    }
}
