using System.Linq;
using System.Text.Json;
using TID3.Services;
using Xunit;

namespace TID3.Tests
{
    public class AcoustIdResponseMapperTests
    {
        // ---- ParseDuration ------------------------------------------------------

        [Fact]
        public void ParseDuration_Null_ReturnsFallback()
        {
            Assert.Equal(200, AcoustIdResponseMapper.ParseDuration(null, 200));
        }

        [Fact]
        public void ParseDuration_Double_RoundsToWholeSeconds()
        {
            Assert.Equal(216, AcoustIdResponseMapper.ParseDuration(215.7, 0));
        }

        [Fact]
        public void ParseDuration_Int_ReturnedDirectly()
        {
            Assert.Equal(215, AcoustIdResponseMapper.ParseDuration(215, 0));
        }

        [Fact]
        public void ParseDuration_JsonElementNumber_ParsedInvariant()
        {
            // System.Text.Json surfaces an untyped number as a JsonElement whose
            // ToString() is invariant ("215.5"); must parse regardless of OS locale.
            var element = JsonDocument.Parse("215.5").RootElement;

            Assert.Equal(216, AcoustIdResponseMapper.ParseDuration(element, 0));
        }

        [Fact]
        public void ParseDuration_InvariantNumericString()
        {
            Assert.Equal(180, AcoustIdResponseMapper.ParseDuration("180.2", 0));
        }

        [Fact]
        public void ParseDuration_Unparseable_ReturnsFallback()
        {
            Assert.Equal(99, AcoustIdResponseMapper.ParseDuration("not-a-number", 99));
        }

        // ---- MapResults ---------------------------------------------------------

        [Fact]
        public void MapResults_NullResults_ReturnsEmpty()
        {
            Assert.Empty(AcoustIdResponseMapper.MapResults(new AcoustIdApiResponse(), durationFallback: 0));
        }

        [Fact]
        public void MapResults_MapsRecordingFields()
        {
            var response = new AcoustIdApiResponse
            {
                Status = "ok",
                Results =
                [
                    new AcoustIdApiResult
                    {
                        Id = "acoustid-1",
                        Score = 0.98,
                        Recordings =
                        [
                            new AcoustIdRecording
                            {
                                Id = "mbid-1",
                                Title = "One More Time",
                                Duration = 320.0,
                                Artists = [ new AcoustIdArtist { Name = "Daft Punk" } ],
                                Releases = [ new AcoustIdRelease { Title = "Discovery" } ]
                            }
                        ]
                    }
                ]
            };

            var result = Assert.Single(AcoustIdResponseMapper.MapResults(response, durationFallback: 0));

            Assert.Equal("acoustid-1", result.TrackId);
            Assert.Equal("mbid-1", result.MusicBrainzId);
            Assert.Equal("One More Time", result.Title);
            Assert.Equal("Daft Punk", result.Artist);
            Assert.Equal("Discovery", result.Album);
            Assert.Equal(320, result.Duration);
            Assert.Equal(0.98, result.Score);
        }

        [Fact]
        public void MapResults_MissingFields_UseUnknownDefaults()
        {
            var response = new AcoustIdApiResponse
            {
                Results =
                [
                    new AcoustIdApiResult
                    {
                        Id = "a1",
                        Score = 0.5,
                        Recordings = [ new AcoustIdRecording { Id = "r1" } ] // no title/artist/release/duration
                    }
                ]
            };

            var result = Assert.Single(AcoustIdResponseMapper.MapResults(response, durationFallback: 250));

            Assert.Equal(AcoustIdResponseMapper.UnknownTitle, result.Title);
            Assert.Equal(AcoustIdResponseMapper.UnknownArtist, result.Artist);
            Assert.Equal(AcoustIdResponseMapper.UnknownAlbum, result.Album);
            Assert.Equal(250, result.Duration);   // falls back to the supplied duration
        }

        [Fact]
        public void MapResults_NoRecordings_ProducesPlaceholder()
        {
            var response = new AcoustIdApiResponse
            {
                Results = [ new AcoustIdApiResult { Id = "abcdef1234", Score = 0.97, Recordings = null } ]
            };

            var result = Assert.Single(AcoustIdResponseMapper.MapResults(response, durationFallback: 200));

            Assert.Equal("abcdef1234", result.MusicBrainzId);   // points back at the AcoustID id
            Assert.Contains("97", result.Title);                // title shows the real score, not a literal
            Assert.StartsWith("AcoustID: abcdef12", result.Album);
            Assert.NotEqual(AcoustIdResponseMapper.UnknownAlbum, result.Album); // so enrichment skips it
            Assert.Equal(200, result.Duration);
        }

        [Fact]
        public void MapResults_OrdersByScoreDescending()
        {
            var response = new AcoustIdApiResponse
            {
                Results =
                [
                    new AcoustIdApiResult { Id = "low",  Score = 0.40, Recordings = [ new AcoustIdRecording { Id = "r-low", Title = "Low" } ] },
                    new AcoustIdApiResult { Id = "high", Score = 0.95, Recordings = [ new AcoustIdRecording { Id = "r-high", Title = "High" } ] },
                    new AcoustIdApiResult { Id = "mid",  Score = 0.70, Recordings = [ new AcoustIdRecording { Id = "r-mid", Title = "Mid" } ] }
                ]
            };

            var results = AcoustIdResponseMapper.MapResults(response, durationFallback: 0);

            Assert.Equal(new[] { 0.95, 0.70, 0.40 }, results.Select(r => r.Score));
        }

        [Fact]
        public void MapResults_LimitsToFiveResultsAndThreeRecordingsEach()
        {
            var response = new AcoustIdApiResponse
            {
                Results = Enumerable.Range(0, 8).Select(i => new AcoustIdApiResult
                {
                    Id = $"a{i}",
                    Score = 0.5,
                    Recordings = Enumerable.Range(0, 5)
                        .Select(j => new AcoustIdRecording { Id = $"r{i}-{j}", Title = $"T{i}-{j}" })
                        .ToArray()
                }).ToArray()
            };

            var results = AcoustIdResponseMapper.MapResults(response, durationFallback: 0);

            // 5 results x 3 recordings each = 15
            Assert.Equal(15, results.Count);
        }
    }
}
