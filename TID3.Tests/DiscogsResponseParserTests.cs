using System.Linq;
using System.Text.Json;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    public class DiscogsResponseParserTests
    {
        // The returned JsonElement keeps its JsonDocument alive, so this is safe.
        private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

        // ---- ParseSearchResults -------------------------------------------------

        [Fact]
        public void ParseSearchResults_NoResultsProperty_ReturnsEmpty()
        {
            Assert.Empty(DiscogsResponseParser.ParseSearchResults(Json("{}")));
        }

        [Fact]
        public void ParseSearchResults_MapsEachResult()
        {
            var root = Json(@"{
                ""results"": [
                    { ""id"": 1, ""title"": ""Daft Punk - Discovery"", ""year"": 2001 },
                    { ""id"": 2, ""title"": ""Justice - Cross"", ""year"": ""2007"" }
                ]
            }");

            var releases = DiscogsResponseParser.ParseSearchResults(root);

            Assert.Equal(2, releases.Count);
            Assert.Equal(1, releases[0].Id);
            Assert.Equal("Discovery", releases[0].Title);
            Assert.Equal("Daft Punk", releases[0].Artist);
            Assert.Equal("2001", releases[0].Year);
        }

        // ---- ExtractAlbumFromTitle ---------------------------------------------

        [Theory]
        [InlineData("Daft Punk - Discovery", "Discovery")]
        [InlineData("Artist - Multi - Part Album", "Multi - Part Album")]
        [InlineData("NoSeparatorHere", "NoSeparatorHere")]
        [InlineData("", "")]
        public void ExtractAlbumFromTitle_Cases(string input, string expected)
        {
            Assert.Equal(expected, DiscogsResponseParser.ExtractAlbumFromTitle(input));
        }

        // ---- GetArtist ----------------------------------------------------------

        [Fact]
        public void GetArtist_FromBasicInformationArtists()
        {
            var el = Json(@"{ ""basic_information"": { ""artists"": [
                { ""name"": ""Daft Punk"" }, { ""name"": ""Pharrell"" } ] } }");

            Assert.Equal("Daft Punk, Pharrell", DiscogsResponseParser.GetArtist(el));
        }

        [Fact]
        public void GetArtist_FromTitlePrefix_WhenNoStructuredArtist()
        {
            var el = Json(@"{ ""title"": ""Justice - Cross"" }");

            Assert.Equal("Justice", DiscogsResponseParser.GetArtist(el));
        }

        [Fact]
        public void GetArtist_UnknownArtist_WhenNothingUsable()
        {
            Assert.Equal("Unknown Artist", DiscogsResponseParser.GetArtist(Json(@"{ ""title"": ""SingleWord"" }")));
        }

        // ---- GetYear ------------------------------------------------------------

        [Theory]
        [InlineData(@"{ ""year"": 1997 }", "1997")]
        [InlineData(@"{ ""year"": ""1997"" }", "1997")]
        [InlineData(@"{ ""year"": true }", "")]
        [InlineData(@"{}", "")]
        public void GetYear_Cases(string json, string expected)
        {
            Assert.Equal(expected, DiscogsResponseParser.GetYear(Json(json)));
        }

        // ---- GetTrackCount ------------------------------------------------------

        [Fact]
        public void GetTrackCount_FromTracklistArrayLength()
        {
            var el = Json(@"{ ""tracklist"": [ {}, {}, {} ] }");

            Assert.Equal(3, DiscogsResponseParser.GetTrackCount(el));
        }

        [Fact]
        public void GetTrackCount_FromFormatDescriptionText()
        {
            var el = Json(@"{ ""format"": [ ""CD, Album, 12 tracks"" ] }");

            Assert.Equal(12, DiscogsResponseParser.GetTrackCount(el));
        }

        [Fact]
        public void GetTrackCount_ZeroWhenUnavailable()
        {
            Assert.Equal(0, DiscogsResponseParser.GetTrackCount(Json(@"{ ""title"": ""An Album"" }")));
        }

        // ---- GetCoverArtUrl -----------------------------------------------------

        [Fact]
        public void GetCoverArtUrl_PrefersCoverImage()
        {
            var el = Json(@"{ ""cover_image"": ""http://img/cover.jpg"", ""thumb"": ""http://img/thumb.jpg"" }");

            Assert.Equal("http://img/cover.jpg", DiscogsResponseParser.GetCoverArtUrl(el));
        }

        [Fact]
        public void GetCoverArtUrl_FallsBackToThumb()
        {
            var el = Json(@"{ ""cover_image"": """", ""thumb"": ""http://img/thumb.jpg"" }");

            Assert.Equal("http://img/thumb.jpg", DiscogsResponseParser.GetCoverArtUrl(el));
        }

        [Fact]
        public void GetCoverArtUrl_NullWhenAbsent()
        {
            Assert.Null(DiscogsResponseParser.GetCoverArtUrl(Json("{}")));
        }

        // ---- GetGenre -----------------------------------------------------------

        [Fact]
        public void GetGenre_JoinsGenreArray()
        {
            var el = Json(@"{ ""genre"": [ ""Electronic"", ""House"" ] }");

            Assert.Equal("Electronic, House", DiscogsResponseParser.GetGenre(el));
        }

        [Fact]
        public void GetGenre_EmptyWhenAbsent()
        {
            Assert.Equal("", DiscogsResponseParser.GetGenre(Json("{}")));
        }
    }
}
