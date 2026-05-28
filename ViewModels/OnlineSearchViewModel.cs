// OnlineSearchViewModel.cs - Orchestrates online metadata search (MusicBrainz / Discogs),
// scoring, result formatting and per-file caching, independently of the WPF view.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TID3.Models;
using TID3.Services;

namespace TID3.ViewModels
{
    /// <summary>
    /// Drives the online-source search feature: queries MusicBrainz/Discogs for the
    /// selected files, scores each candidate against its file, formats a display
    /// string, caches results per file, and exposes the sorted results for the
    /// currently selected file. View concerns (cursor, dialogs, cover-art image
    /// loading) are left to the view, which subscribes to the cover-art events.
    /// </summary>
    public class OnlineSearchViewModel : ViewModelBase
    {
        private const int MaxCandidatesPerFile = 5;
        private const int TopResultsPerFile = 3;

        private static readonly Regex ScorePattern = new(@"\[(\d+\.?\d*)%\]", RegexOptions.Compiled);

        private readonly IMusicBrainzService _musicBrainz;
        private readonly IDiscogsService _discogs;
        private readonly MatchScoringService _scorer;
        private readonly OnlineSourceCacheManager _cache;

        public OnlineSearchViewModel(
            IMusicBrainzService musicBrainz,
            IDiscogsService discogs,
            MatchScoringService scorer,
            OnlineSourceCacheManager cache)
        {
            _musicBrainz = musicBrainz;
            _discogs = discogs;
            _scorer = scorer;
            _cache = cache;
        }

        /// <summary>Results shown for the currently selected file, sorted by score.</summary>
        public ObservableCollection<OnlineSourceItem> OnlineSourceItems { get; } = new();

        /// <summary>The file whose cached results are currently displayed.</summary>
        public string? SelectedFilePath { get; set; }

        /// <summary>Raised after a MusicBrainz result is added, so the view can load its cover art.</summary>
        public event Action<MusicBrainzRelease>? MusicBrainzCoverArtRequested;

        /// <summary>Raised after a Discogs result is added, so the view can load its cover art.</summary>
        public event Action<DiscogsRelease>? DiscogsCoverArtRequested;

        // ---- Pure display-name formatting --------------------------------------

        /// <summary>
        /// Formats a score as a percentage using the invariant culture (always a '.'
        /// decimal separator) so the value round-trips through <see cref="ExtractScore"/>
        /// regardless of the OS locale.
        /// </summary>
        public static string FormatScorePercent(double score)
            => $"[{(score * 100).ToString("F1", CultureInfo.InvariantCulture)}%]";

        public static string BuildMusicBrainzDisplayName(MusicBrainzRelease release, double score, string fileName)
        {
            var name = $"MB: {release.Artist} - {release.Title} ({release.Date})";
            if (release.TrackCount > 0) name += $" [{release.TrackCount}T]";
            name += $" [For: {fileName}]";
            if (score > 0) name += $" {FormatScorePercent(score)}";
            return name;
        }

        public static string BuildDiscogsDisplayName(DiscogsRelease release, double score, string fileName)
        {
            var name = $"DC: {release.Artist} - {release.Title} ({release.Year})";
            if (release.TrackCount > 0) name += $" [{release.TrackCount}T]";
            name += $" [For: {fileName}]";
            if (score > 0) name += $" {FormatScorePercent(score)}";
            return name;
        }

        /// <summary>Extracts the percentage from a display name (e.g. "[85.2%]" -> 85.2).</summary>
        public static double ExtractScore(string displayName)
        {
            var match = ScorePattern.Match(displayName);
            if (match.Success && double.TryParse(match.Groups[1].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out double score))
            {
                return score;
            }
            return 0.0;
        }

        // ---- Pure result building ----------------------------------------------

        /// <summary>
        /// Scores the releases against <paramref name="file"/>, keeps the top results,
        /// and builds the corresponding <see cref="OnlineSourceItem"/>s (highest first).
        /// </summary>
        public List<OnlineSourceItem> BuildMusicBrainzItems(AudioFileInfo file, IEnumerable<MusicBrainzRelease> releases, int loadedTrackCount)
        {
            return releases
                .Take(MaxCandidatesPerFile)
                .Select(r => new { Release = r, Score = _scorer.CalculateMatchScore(file, r, loadedTrackCount) })
                .OrderByDescending(x => x.Score)
                .Take(TopResultsPerFile)
                .Select(x => new OnlineSourceItem
                {
                    DisplayName = BuildMusicBrainzDisplayName(x.Release, x.Score, file.FileName),
                    Source = x.Release,
                    SourceType = "MusicBrainz",
                    Data = file
                })
                .ToList();
        }

        public List<OnlineSourceItem> BuildDiscogsItems(AudioFileInfo file, IEnumerable<DiscogsRelease> releases, int loadedTrackCount)
        {
            return releases
                .Take(MaxCandidatesPerFile)
                .Select(r => new { Release = r, Score = _scorer.CalculateMatchScore(file, r, loadedTrackCount) })
                .OrderByDescending(x => x.Score)
                .Take(TopResultsPerFile)
                .Select(x => new OnlineSourceItem
                {
                    DisplayName = BuildDiscogsDisplayName(x.Release, x.Score, file.FileName),
                    Source = x.Release,
                    SourceType = "Discogs",
                    Data = file
                })
                .ToList();
        }

        // ---- Cache + visible collection ----------------------------------------

        /// <summary>Adds items to a file's cache and refreshes the visible list if it is selected.</summary>
        public void AddResults(AudioFileInfo file, IReadOnlyList<OnlineSourceItem> items)
        {
            var current = _cache.GetResults(file.FilePath);
            current.AddRange(items);
            _cache.StoreResults(file.FilePath, current);

            if (SelectedFilePath == file.FilePath)
                RefreshVisible();
        }

        /// <summary>Removes results of a source type from the visible list and all caches.</summary>
        public void ClearResults(string sourceType)
        {
            foreach (var item in OnlineSourceItems.Where(x => x.SourceType == sourceType).ToList())
                OnlineSourceItems.Remove(item);

            foreach (var filePath in _cache.GetCachedFilePaths().ToList())
            {
                var filtered = _cache.GetResults(filePath).Where(x => x.SourceType != sourceType).ToList();
                _cache.StoreResults(filePath, filtered);
            }
        }

        /// <summary>Repopulates the visible list from the selected file's cache, sorted by score.</summary>
        public void RefreshVisible()
        {
            OnlineSourceItems.Clear();
            if (SelectedFilePath == null) return;

            var sorted = _cache.GetResults(SelectedFilePath)
                .OrderByDescending(r => ExtractScore(r.DisplayName))
                .ThenBy(r => r.DisplayName);

            foreach (var result in sorted)
            {
                _ = result.ScoreCategory; // force recalculation of the colour category
                OnlineSourceItems.Add(result);
            }
        }

        // ---- Async orchestration -----------------------------------------------

        /// <summary>
        /// Searches MusicBrainz for each file, caching and displaying scored results.
        /// Returns the number of files that produced at least one result.
        /// </summary>
        public async Task<int> SearchMusicBrainzAsync(
            IReadOnlyList<AudioFileInfo> files, int loadedTrackCount,
            Action<string>? status = null, Func<int, Task>? interRequestDelay = null)
        {
            ClearResults("MusicBrainz");

            int processed = 0, successful = 0;
            foreach (var file in files)
            {
                processed++;
                status?.Invoke($"MusicBrainz search: {processed}/{files.Count} - {file.FileName}");

                var query = $"{file.Artist} {file.Album}".Trim();
                if (!string.IsNullOrEmpty(query))
                {
                    var releases = await _musicBrainz.SearchReleases(query);
                    if (releases.Count > 0)
                    {
                        var items = BuildMusicBrainzItems(file, releases, loadedTrackCount);
                        AddResults(file, items);
                        foreach (var item in items)
                            MusicBrainzCoverArtRequested?.Invoke((MusicBrainzRelease)item.Source);
                        successful++;
                    }
                }

                if (processed < files.Count)
                    await (interRequestDelay?.Invoke(1100) ?? Task.Delay(1100)); // MusicBrainz rate limit: ~1 req/s
            }

            status?.Invoke($"MusicBrainz search completed: {successful}/{files.Count} files found results");
            return successful;
        }

        /// <summary>
        /// Searches Discogs for each file, caching and displaying scored results.
        /// Returns the number of files that produced at least one result.
        /// </summary>
        public async Task<int> SearchDiscogsAsync(
            IReadOnlyList<AudioFileInfo> files, int loadedTrackCount,
            Action<string>? status = null, Func<int, Task>? interRequestDelay = null)
        {
            ClearResults("Discogs");

            int processed = 0, successful = 0;
            foreach (var file in files)
            {
                processed++;
                status?.Invoke($"Discogs search: {processed}/{files.Count} - {file.FileName}");

                var query = $"{file.Artist} {file.Album}".Trim();
                if (!string.IsNullOrEmpty(query))
                {
                    var releases = await _discogs.SearchReleases(query);
                    if (releases.Count > 0)
                    {
                        var items = BuildDiscogsItems(file, releases, loadedTrackCount);
                        AddResults(file, items);
                        foreach (var item in items)
                        {
                            var release = (DiscogsRelease)item.Source;
                            if (!string.IsNullOrEmpty(release.CoverArtUrl))
                                DiscogsCoverArtRequested?.Invoke(release);
                        }
                        successful++;
                    }
                }

                if (processed < files.Count)
                    await (interRequestDelay?.Invoke(250) ?? Task.Delay(250));
            }

            status?.Invoke($"Discogs search completed: {successful}/{files.Count} files found results");
            return successful;
        }
    }
}
