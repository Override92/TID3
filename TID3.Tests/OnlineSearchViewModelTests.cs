using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using TID3.Models;
using TID3.Services;
using TID3.ViewModels;
using Xunit;

namespace TID3.Tests
{
    public class OnlineSearchViewModelTests
    {
        // ---- Fakes --------------------------------------------------------------

        private sealed class FakeMusicBrainzService : IMusicBrainzService
        {
            public Dictionary<string, List<MusicBrainzRelease>> ResultsByQuery = new();
            public Task<List<MusicBrainzRelease>> SearchReleases(string query)
                => Task.FromResult(ResultsByQuery.TryGetValue(query, out var r) ? r : new List<MusicBrainzRelease>());
            public Task<MusicBrainzRelease?> GetReleaseDetails(string releaseId) => Task.FromResult<MusicBrainzRelease?>(null);
            public Task<string?> GetCoverArtUrl(string releaseId) => Task.FromResult<string?>(null);
            public Task<BitmapImage?> LoadCoverArtImage(string? imageUrl) => Task.FromResult<BitmapImage?>(null);
            public void RefreshSettings() { }
        }

        private sealed class FakeDiscogsService : IDiscogsService
        {
            public Dictionary<string, List<DiscogsRelease>> ResultsByQuery = new();
            public Task<List<DiscogsRelease>> SearchReleases(string query)
                => Task.FromResult(ResultsByQuery.TryGetValue(query, out var r) ? r : new List<DiscogsRelease>());
            public void RefreshSettings() { }
        }

        private static OnlineSearchViewModel NewVm(
            FakeMusicBrainzService? mb = null, FakeDiscogsService? dc = null)
            => new(mb ?? new FakeMusicBrainzService(), dc ?? new FakeDiscogsService(),
                   new MatchScoringService(), new OnlineSourceCacheManager());

        private static Task NoDelay(int _) => Task.CompletedTask;

        // ---- FormatScorePercent / ExtractScore ----------------------------------

        [Fact]
        public void FormatScorePercent_UsesInvariantDot()
        {
            Assert.Equal("[85.2%]", OnlineSearchViewModel.FormatScorePercent(0.852));
        }

        [Fact]
        public void FormatAndExtractScore_RoundTrip_OnGermanLocale()
        {
            // The score-sort bug: a comma-formatted percent wouldn't be re-parsed.
            // FormatScorePercent must emit a dot even under de-DE so ExtractScore reads it.
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var formatted = OnlineSearchViewModel.FormatScorePercent(0.852);
                Assert.Equal("[85.2%]", formatted);
                Assert.Equal(85.2, OnlineSearchViewModel.ExtractScore($"MB: x {formatted}"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void ExtractScore_NoPercent_ReturnsZero()
        {
            Assert.Equal(0.0, OnlineSearchViewModel.ExtractScore("MB: Artist - Album [For: x.mp3]"));
        }

        // ---- Display names ------------------------------------------------------

        [Fact]
        public void BuildMusicBrainzDisplayName_IncludesTrackCountAndScore()
        {
            var release = new MusicBrainzRelease { Artist = "Radiohead", Title = "OK Computer", Date = "1997", TrackCount = 12 };

            var name = OnlineSearchViewModel.BuildMusicBrainzDisplayName(release, 0.95, "track01.mp3");

            Assert.Equal("MB: Radiohead - OK Computer (1997) [12T] [For: track01.mp3] [95.0%]", name);
        }

        [Fact]
        public void BuildDiscogsDisplayName_OmitsScoreWhenZero_AndTrackCountWhenZero()
        {
            var release = new DiscogsRelease { Artist = "Daft Punk", Title = "Discovery", Year = "2001", TrackCount = 0 };

            var name = OnlineSearchViewModel.BuildDiscogsDisplayName(release, 0.0, "a.mp3");

            Assert.Equal("DC: Daft Punk - Discovery (2001) [For: a.mp3]", name);
        }

        // ---- BuildMusicBrainzItems ---------------------------------------------

        [Fact]
        public void BuildMusicBrainzItems_OrdersByScore_AndLimitsToThree()
        {
            var vm = NewVm();
            var file = new AudioFileInfo { Artist = "Radiohead", Album = "OK Computer", Year = 1997 };
            var releases = new List<MusicBrainzRelease>
            {
                new() { Artist = "Wrong", Title = "Other", Date = "2010", TrackCount = 1 },
                new() { Artist = "Radiohead", Title = "OK Computer", Date = "1997", TrackCount = 12 }, // best
                new() { Artist = "Radiohead", Title = "Kid A", Date = "2000", TrackCount = 11 },
                new() { Artist = "Nope", Title = "Nope", Date = "1980", TrackCount = 4 }
            };

            var items = vm.BuildMusicBrainzItems(file, releases, loadedTrackCount: 12);

            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.Equal("MusicBrainz", i.SourceType));
            Assert.All(items, i => Assert.Same(file, i.Data));
            Assert.Contains("OK Computer", items[0].DisplayName); // best match ranked first
        }

        // ---- Cache + visible ----------------------------------------------------

        [Fact]
        public void AddResults_RefreshesVisible_OnlyForSelectedFile()
        {
            var vm = NewVm();
            var file = new AudioFileInfo { FilePath = @"C:\a.mp3", Artist = "A", Album = "B" };

            vm.SelectedFilePath = @"C:\other.mp3";
            vm.AddResults(file, new[] { new OnlineSourceItem { DisplayName = "x", SourceType = "MusicBrainz" } });
            Assert.Empty(vm.OnlineSourceItems); // not the selected file -> not shown

            vm.SelectedFilePath = @"C:\a.mp3";
            vm.RefreshVisible();
            Assert.Single(vm.OnlineSourceItems); // now shown from cache
        }

        [Fact]
        public void RefreshVisible_SortsByScoreDescending()
        {
            var vm = NewVm();
            var file = new AudioFileInfo { FilePath = @"C:\a.mp3" };
            vm.SelectedFilePath = @"C:\a.mp3";

            vm.AddResults(file, new[]
            {
                new OnlineSourceItem { DisplayName = "MB: low [50.0%]", SourceType = "MusicBrainz" },
                new OnlineSourceItem { DisplayName = "MB: high [90.0%]", SourceType = "MusicBrainz" },
                new OnlineSourceItem { DisplayName = "MB: mid [70.0%]", SourceType = "MusicBrainz" }
            });

            Assert.Equal(
                new[] { "MB: high [90.0%]", "MB: mid [70.0%]", "MB: low [50.0%]" },
                vm.OnlineSourceItems.Select(i => i.DisplayName));
        }

        [Fact]
        public void ClearResults_RemovesOnlyThatSourceType()
        {
            var vm = NewVm();
            var file = new AudioFileInfo { FilePath = @"C:\a.mp3" };
            vm.SelectedFilePath = @"C:\a.mp3";
            vm.AddResults(file, new[]
            {
                new OnlineSourceItem { DisplayName = "MB: x [80.0%]", SourceType = "MusicBrainz" },
                new OnlineSourceItem { DisplayName = "DC: y [80.0%]", SourceType = "Discogs" }
            });

            vm.ClearResults("MusicBrainz");

            Assert.Single(vm.OnlineSourceItems);
            Assert.Equal("Discogs", vm.OnlineSourceItems[0].SourceType);
        }

        // ---- Async search -------------------------------------------------------

        [Fact]
        public async Task SearchMusicBrainzAsync_PopulatesResults_AndReturnsSuccessCount()
        {
            var mb = new FakeMusicBrainzService();
            mb.ResultsByQuery["Radiohead OK Computer"] =
            [
                new MusicBrainzRelease { Artist = "Radiohead", Title = "OK Computer", Date = "1997", TrackCount = 12 }
            ];
            var vm = NewVm(mb);
            var file = new AudioFileInfo { FilePath = @"C:\a.mp3", Artist = "Radiohead", Album = "OK Computer", Year = 1997 };
            vm.SelectedFilePath = @"C:\a.mp3";

            var success = await vm.SearchMusicBrainzAsync(new[] { file }, loadedTrackCount: 12, interRequestDelay: NoDelay);

            Assert.Equal(1, success);
            Assert.Single(vm.OnlineSourceItems);
            Assert.Contains("OK Computer", vm.OnlineSourceItems[0].DisplayName);
        }

        [Fact]
        public async Task SearchMusicBrainzAsync_RaisesCoverArtEventPerResult()
        {
            var mb = new FakeMusicBrainzService();
            mb.ResultsByQuery["A B"] = [ new MusicBrainzRelease { Artist = "A", Title = "B", Date = "2000", TrackCount = 1 } ];
            var vm = NewVm(mb);
            var raised = 0;
            vm.MusicBrainzCoverArtRequested += _ => raised++;
            var file = new AudioFileInfo { FilePath = @"C:\a.mp3", Artist = "A", Album = "B" };

            await vm.SearchMusicBrainzAsync(new[] { file }, 1, interRequestDelay: NoDelay);

            Assert.Equal(1, raised);
        }

        [Fact]
        public async Task SearchMusicBrainzAsync_NoResults_ReturnsZero()
        {
            var vm = NewVm();
            var file = new AudioFileInfo { FilePath = @"C:\a.mp3", Artist = "Unknown", Album = "Nothing" };

            var success = await vm.SearchMusicBrainzAsync(new[] { file }, 0, interRequestDelay: NoDelay);

            Assert.Equal(0, success);
            Assert.Empty(vm.OnlineSourceItems);
        }

        [Fact]
        public async Task SearchDiscogsAsync_RaisesCoverEvent_OnlyWhenCoverUrlPresent()
        {
            var dc = new FakeDiscogsService();
            dc.ResultsByQuery["A B"] =
            [
                new DiscogsRelease { Artist = "A", Title = "B", Year = "2000", CoverArtUrl = "http://img/c.jpg" },
                new DiscogsRelease { Artist = "A", Title = "B (Reissue)", Year = "2001", CoverArtUrl = null }
            ];
            var vm = NewVm(dc: dc);
            var raised = 0;
            vm.DiscogsCoverArtRequested += _ => raised++;
            var file = new AudioFileInfo { FilePath = @"C:\a.mp3", Artist = "A", Album = "B" };
            vm.SelectedFilePath = @"C:\a.mp3";

            await vm.SearchDiscogsAsync(new[] { file }, 1, interRequestDelay: NoDelay);

            Assert.Equal(2, vm.OnlineSourceItems.Count);
            Assert.Equal(1, raised); // only the result with a cover URL
        }
    }
}
