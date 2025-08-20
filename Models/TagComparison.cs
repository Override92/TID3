using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TID3.Services;
using TID3.Utils;

namespace TID3.Models
{
    public class TagComparisonItem : INotifyPropertyChanged
    {
        private string _fieldName = "";
        private string _originalValue = "";
        private string _newValue = "";
        private bool _isChanged;
        private bool _isNew;
        private bool _isAccepted;
        private bool _isRejected;

        public string FieldName
        {
            get => _fieldName;
            set { _fieldName = value; OnPropertyChanged(); }
        }

        public string OriginalValue
        {
            get => _originalValue ?? "";
            set { _originalValue = value; OnPropertyChanged(); UpdateStatus(); }
        }

        public string NewValue
        {
            get => _newValue ?? "";
            set { _newValue = value; OnPropertyChanged(); UpdateStatus(); }
        }

        public bool IsChanged
        {
            get => _isChanged;
            private set { _isChanged = value; OnPropertyChanged(); }
        }

        public bool IsNew
        {
            get => _isNew;
            private set { _isNew = value; OnPropertyChanged(); }
        }

        public bool IsAccepted
        {
            get => _isAccepted;
            set { _isAccepted = value; OnPropertyChanged(); UpdateStatus(); }
        }

        public bool IsRejected
        {
            get => _isRejected;
            set { _isRejected = value; OnPropertyChanged(); UpdateStatus(); }
        }

        public string StatusIcon { get; private set; } = "";
        public string StatusText { get; private set; } = "";
        public bool CanAccept { get; private set; }
        public bool CanReject { get; private set; }

        private void UpdateStatus()
        {
            var originalEmpty = string.IsNullOrWhiteSpace(OriginalValue);
            var newEmpty = string.IsNullOrWhiteSpace(NewValue);
            var valuesEqual = string.Equals(OriginalValue?.Trim(), NewValue?.Trim(), StringComparison.OrdinalIgnoreCase);

            IsChanged = !originalEmpty && !newEmpty && !valuesEqual;
            IsNew = originalEmpty && !newEmpty;

            if (IsAccepted)
            {
                StatusIcon = "✅";
                StatusText = "Accepted";
                CanAccept = false;
                CanReject = true;
            }
            else if (IsRejected)
            {
                StatusIcon = "❌";
                StatusText = "Rejected";
                CanAccept = true;
                CanReject = false;
            }
            else if (valuesEqual)
            {
                StatusIcon = "=";
                StatusText = "Same";
                CanAccept = false;
                CanReject = false;
            }
            else if (IsNew)
            {
                StatusIcon = "✨";
                StatusText = "New";
                CanAccept = true;
                CanReject = true;
            }
            else if (IsChanged)
            {
                StatusIcon = "🔄";
                StatusText = "Changed";
                CanAccept = true;
                CanReject = true;
            }
            else if (newEmpty && !originalEmpty)
            {
                StatusIcon = "🗑️";
                StatusText = "Removed";
                CanAccept = true;
                CanReject = true;
            }
            else
            {
                StatusIcon = "—";
                StatusText = "No change";
                CanAccept = false;
                CanReject = false;
            }

            OnPropertyChanged(nameof(StatusIcon));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanAccept));
            OnPropertyChanged(nameof(CanReject));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class ChangeHistoryItem
    {
        public DateTime Timestamp { get; set; }
        public string Action { get; set; }
        public string Details { get; set; }
        public string Source { get; set; }

        public ChangeHistoryItem(string action, string details, string source = "Manual")
        {
            Timestamp = DateTime.Now;
            Action = action;
            Details = details;
            Source = source;
        }
    }

    public partial class AudioFileInfo
    {
        // Add comparison and history functionality
        private TagSnapshot? _originalTags;
        private ObservableCollection<TagComparisonItem>? _tagComparison;
        private ObservableCollection<ChangeHistoryItem>? _changeHistory;
        private string _statusText = "Ready";
        private Color _statusColor = Colors.LimeGreen;
        private string? _lastModified;

        public ObservableCollection<TagComparisonItem> TagComparison
        {
            get => _tagComparison ??= [];
            set { _tagComparison = value; OnPropertyChanged(); }
        }

        public ObservableCollection<ChangeHistoryItem> ChangeHistory
        {
            get => _changeHistory ??= [];
            set { _changeHistory = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public Color StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public string? LastModified
        {
            get => _lastModified;
            set { _lastModified = value; OnPropertyChanged(); }
        }

        public void CreateSnapshot()
        {
            // Only create snapshot when actually needed
            if (_originalTags == null)
            {
                _originalTags = new TagSnapshot
                {
                    Title = Title,
                    Artist = Artist,
                    Album = Album,
                    AlbumArtist = AlbumArtist,
                    Genre = Genre,
                    Year = Year,
                    Track = Track,
                    Comment = Comment
                };

                UpdateLastModified();
                // Don't add to history unnecessarily - only when comparison is needed
            }
        }

        public void UpdateComparison(TagSnapshot newTags, string source = "Unknown")
        {
            if (_originalTags == null)
                CreateSnapshot();

            TagComparison?.Clear();

            var comparisons = new[]
            {
                new TagComparisonItem { FieldName = "Title", OriginalValue = _originalTags?.Title ?? "", NewValue = newTags.Title },
                new TagComparisonItem { FieldName = "Artist", OriginalValue = _originalTags?.Artist ?? "", NewValue = newTags.Artist },
                new TagComparisonItem { FieldName = "Album", OriginalValue = _originalTags?.Album ?? "", NewValue = newTags.Album },
                new TagComparisonItem { FieldName = "Album Artist", OriginalValue = _originalTags?.AlbumArtist ?? "", NewValue = newTags.AlbumArtist },
                new TagComparisonItem { FieldName = "Genre", OriginalValue = _originalTags?.Genre ?? "", NewValue = newTags.Genre },
                new TagComparisonItem { FieldName = "Year", OriginalValue = _originalTags?.Year.ToString() ?? "0", NewValue = newTags.Year.ToString() },
                new TagComparisonItem { FieldName = "Track", OriginalValue = _originalTags?.Track.ToString() ?? "0", NewValue = newTags.Track.ToString() },
                new TagComparisonItem { FieldName = "Comment", OriginalValue = _originalTags?.Comment ?? "", NewValue = newTags.Comment }
            };

            foreach (var comparison in comparisons)
            {
                TagComparison?.Add(comparison);
            }

            // Notify UI that TagComparison has been updated
            OnPropertyChanged(nameof(TagComparison));

            var changedCount = comparisons.Count(c => c.IsChanged || c.IsNew);
            if (changedCount > 0)
            {
                StatusText = $"{changedCount} changes detected";
                StatusColor = Colors.Orange;
                AddToHistory("Changes Detected", $"Found {changedCount} potential changes from {source}");
            }
            else
            {
                StatusText = "No changes detected";
                StatusColor = Colors.LimeGreen;
                AddToHistory("No Changes", $"No differences found from {source}");
            }
        }

        public void AcceptChange(TagComparisonItem item)
        {
            if (item == null || !item.CanAccept) return;

            item.IsAccepted = true;
            item.IsRejected = false;

            // Apply the change to the actual properties
            switch (item.FieldName)
            {
                case "Title":
                    Title = item.NewValue;
                    break;
                case "Artist":
                    Artist = item.NewValue;
                    break;
                case "Album":
                    Album = item.NewValue;
                    break;
                case "Album Artist":
                    AlbumArtist = item.NewValue;
                    break;
                case "Genre":
                    Genre = item.NewValue;
                    break;
                case "Year":
                    if (uint.TryParse(item.NewValue, out uint year))
                        Year = year;
                    break;
                case "Track":
                    if (uint.TryParse(item.NewValue, out uint track))
                        Track = track;
                    break;
                case "Comment":
                    Comment = item.NewValue;
                    break;
            }

            UpdateLastModified();
            AddToHistory("Change Accepted", $"{item.FieldName}: '{item.OriginalValue}' → '{item.NewValue}'");
            UpdateOverallStatus();
        }

        public void RejectChange(TagComparisonItem item)
        {
            if (item == null || !item.CanReject) return;

            item.IsAccepted = false;
            item.IsRejected = true;

            AddToHistory("Change Rejected", $"{item.FieldName}: Kept original value '{item.OriginalValue}'");
            UpdateOverallStatus();
        }

        public void AcceptAllChanges()
        {
            var pendingChanges = TagComparison.Where(c => c.CanAccept).ToList();
            foreach (var item in pendingChanges)
            {
                AcceptChange(item);
            }

            if (pendingChanges.Count > 0)
            {
                AddToHistory("Bulk Accept", $"Accepted {pendingChanges.Count} changes");
            }
        }

        public void RevertAllChanges()
        {
            var acceptedChanges = TagComparison.Where(c => c.IsAccepted).ToList();

            // Revert to original values
            if (_originalTags != null)
            {
                Title = _originalTags.Title;
                Artist = _originalTags.Artist;
                Album = _originalTags.Album;
                AlbumArtist = _originalTags.AlbumArtist;
                Genre = _originalTags.Genre;
                Year = _originalTags.Year;
                Track = _originalTags.Track;
                Comment = _originalTags.Comment;
            }

            // Reset all comparison states
            foreach (var item in TagComparison)
            {
                item.IsAccepted = false;
                item.IsRejected = false;
            }

            if (acceptedChanges.Count > 0)
            {
                AddToHistory("Bulk Revert", $"Reverted {acceptedChanges.Count} changes to original values");
            }

            UpdateOverallStatus();
        }

        public string GetComparisonSummary()
        {
            if (TagComparison == null || !TagComparison.Any())
                return "No comparison data available";

            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"Tag Comparison for: {FileName}");
            summary.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            summary.AppendLine();

            summary.AppendLine("Field\t\tOriginal\t\tNew\t\tStatus");
            summary.AppendLine(new string('-', 80));

            foreach (var item in TagComparison)
            {
                summary.AppendLine($"{item.FieldName,-15}\t{item.OriginalValue,-20}\t{item.NewValue,-20}\t{item.StatusText}");
            }

            var changedCount = TagComparison.Count(c => c.IsChanged || c.IsNew);
            var acceptedCount = TagComparison.Count(c => c.IsAccepted);
            var rejectedCount = TagComparison.Count(c => c.IsRejected);

            summary.AppendLine();
            summary.AppendLine($"Summary: {changedCount} changes detected, {acceptedCount} accepted, {rejectedCount} rejected");

            return summary.ToString();
        }

        private void UpdateOverallStatus()
        {
            var pending = TagComparison.Count(c => c.CanAccept);
            var accepted = TagComparison.Count(c => c.IsAccepted);

            if (pending > 0)
            {
                StatusText = $"{pending} changes pending";
                StatusColor = Colors.Orange;
            }
            else if (accepted > 0)
            {
                StatusText = $"{accepted} changes applied";
                StatusColor = Colors.LimeGreen;
            }
            else
            {
                StatusText = "No changes";
                StatusColor = Colors.Gray;
            }
        }

        private void UpdateLastModified()
        {
            LastModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public void ClearComparison()
        {
            TagComparison?.Clear();
            _originalTags = null;
            OnPropertyChanged(nameof(TagComparison));
        }

        public void ClearHistory()
        {
            ChangeHistory?.Clear();
            OnPropertyChanged(nameof(ChangeHistory));
        }

        public void Cleanup()
        {
            ClearComparison();
            ClearHistory();
            // Clean up album cover image to prevent memory leaks
            AlbumCover = null;
        }

        private void AddToHistory(string action, string details)
        {
            ChangeHistory.Insert(0, new ChangeHistoryItem(action, details));

            // Keep only last 50 entries
            while (ChangeHistory.Count > 50)
            {
                ChangeHistory.RemoveAt(ChangeHistory.Count - 1);
            }
        }
    }

    public class TagSnapshot
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string AlbumArtist { get; set; } = "";
        public string Genre { get; set; } = "";
        public uint Year { get; set; } = 0;
        public uint Track { get; set; } = 0;
        public string Comment { get; set; } = "";

        public static TagSnapshot FromAudioFile(AudioFileInfo audioFile)
        {
            return new TagSnapshot
            {
                Title = audioFile.Title,
                Artist = audioFile.Artist,
                Album = audioFile.Album,
                AlbumArtist = audioFile.AlbumArtist,
                Genre = audioFile.Genre,
                Year = audioFile.Year,
                Track = audioFile.Track,
                Comment = audioFile.Comment
            };
        }

        public static TagSnapshot FromMusicBrainzRelease(MusicBrainzRelease release, MusicBrainzTrack? track = null, AudioFileInfo? preserveFrom = null)
        {
            return new TagSnapshot
            {
                Title = track?.Title ?? preserveFrom?.Title ?? "", // Preserve existing title if no track-specific title
                Artist = track?.Artist ?? release.Artist,
                Album = release.Title,
                AlbumArtist = release.Artist,
                Genre = preserveFrom?.Genre ?? "", // MusicBrainz basic search doesn't include genre, preserve existing
                Year = ExtractYearFromDate(release.Date),
                Track = track != null ? (uint)track.Position : (preserveFrom?.Track ?? 0),
                Comment = preserveFrom?.Comment ?? ""
            };
        }

        public static TagSnapshot FromDiscogsRelease(DiscogsRelease release, DiscogsTrack? track = null, AudioFileInfo? preserveFrom = null)
        {
            return new TagSnapshot
            {
                Title = track?.Title ?? preserveFrom?.Title ?? "", // Preserve existing title if no track-specific title
                Artist = release.Artist,
                Album = release.Title,
                AlbumArtist = release.Artist,
                Genre = !string.IsNullOrEmpty(release.Genre) ? release.Genre : (preserveFrom?.Genre ?? ""), // Use online genre if available
                Year = uint.TryParse(release.Year, out uint year) ? year : (preserveFrom?.Year ?? 0),
                Track = preserveFrom?.Track ?? 0, // Preserve existing track number
                Comment = preserveFrom?.Comment ?? ""
            };
        }

        public static TagSnapshot FromAcoustIdResult(AcoustIdResult result, AudioFileInfo? preserveFrom = null)
        {
            return new TagSnapshot
            {
                Title = !string.IsNullOrEmpty(result.Title) ? result.Title : (preserveFrom?.Title ?? ""),
                Artist = !string.IsNullOrEmpty(result.Artist) ? result.Artist : (preserveFrom?.Artist ?? ""),
                Album = !string.IsNullOrEmpty(result.Album) ? result.Album : (preserveFrom?.Album ?? ""),
                AlbumArtist = !string.IsNullOrEmpty(result.Artist) ? result.Artist : (preserveFrom?.AlbumArtist ?? ""),
                Genre = preserveFrom?.Genre ?? "", // AcoustID doesn't provide genre, preserve existing
                Year = preserveFrom?.Year ?? 0, // AcoustID doesn't provide year, preserve existing
                Track = preserveFrom?.Track ?? 0, // AcoustID doesn't provide track number, preserve existing
                Comment = preserveFrom?.Comment ?? ""
            };
        }

        private static uint ExtractYearFromDate(string? dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString) || dateString.Length < 4)
                return 0;

            var yearString = dateString.Length >= 4 ? dateString[..4] : dateString;
            return uint.TryParse(yearString, out uint year) ? year : 0;
        }
    }

    public enum ComparisonMode
    {
        AllFields,
        ChangedOnly,
        MissingOnly
    }
}