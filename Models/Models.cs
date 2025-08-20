// Models.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace TID3.Models
{
    public class CoverSource
    {
        public string Name { get; set; } = "";
        public BitmapImage? Image { get; set; }
        public string Source { get; set; } = "";
        public string Resolution => Image != null ? $"{Image.PixelWidth} × {Image.PixelHeight}" : "";
    }
    public partial class AudioFileInfo : INotifyPropertyChanged
    {
        private string _title = "";
        private string _artist = "";
        private string _album = "";
        private string _genre = "";
        private uint _year;
        private uint _track;
        private string _albumArtist = "";
        private string _comment = "";
        private string _matchScore = "";
        private BitmapImage? _albumCover;
        private string _coverArtSource = "";
        private BitmapImage? _localCover;
        private BitmapImage? _onlineCover;
        private string _coverResolution = "";
        private bool _useLocalCover = true;
        private string _selectedCoverSource = "";
        private readonly ObservableCollection<CoverSource> _availableCovers = new();

        public string FilePath { get; set; } = "";
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string Duration { get; set; } = "";
        public string Bitrate { get; set; } = "";
        public string FileSize { get; set; } = "";

        public string MatchScore
        {
            get => _matchScore;
            set { _matchScore = value; OnPropertyChanged(); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string Artist
        {
            get => _artist;
            set { _artist = value; OnPropertyChanged(); }
        }

        public string Album
        {
            get => _album;
            set { _album = value; OnPropertyChanged(); }
        }

        public string Genre
        {
            get => _genre;
            set { _genre = value; OnPropertyChanged(); }
        }

        public uint Year
        {
            get => _year;
            set { _year = value; OnPropertyChanged(); }
        }

        public uint Track
        {
            get => _track;
            set { _track = value; OnPropertyChanged(); }
        }

        public string AlbumArtist
        {
            get => _albumArtist;
            set { _albumArtist = value; OnPropertyChanged(); }
        }

        public string Comment
        {
            get => _comment;
            set { _comment = value; OnPropertyChanged(); }
        }

        public BitmapImage? AlbumCover
        {
            get => _albumCover;
            set { _albumCover = value; OnPropertyChanged(); }
        }

        public string CoverArtSource
        {
            get => _coverArtSource;
            set { _coverArtSource = value; OnPropertyChanged(); }
        }

        public BitmapImage? LocalCover
        {
            get => _localCover;
            set { _localCover = value; OnPropertyChanged(); UpdateActiveCover(); }
        }

        public BitmapImage? OnlineCover
        {
            get => _onlineCover;
            set { _onlineCover = value; OnPropertyChanged(); UpdateActiveCover(); }
        }

        public string CoverResolution
        {
            get => _coverResolution;
            set { _coverResolution = value; OnPropertyChanged(); }
        }

        public bool UseLocalCover
        {
            get => _useLocalCover;
            set { _useLocalCover = value; OnPropertyChanged(); UpdateActiveCover(); }
        }

        public bool HasLocalCover => LocalCover != null;
        public bool HasOnlineCover => OnlineCover != null;
        public bool HasMultipleCovers => HasLocalCover && HasOnlineCover;

        public ObservableCollection<CoverSource> AvailableCovers => _availableCovers;

        public string SelectedCoverSource
        {
            get => _selectedCoverSource;
            set 
            { 
                if (_selectedCoverSource != value)
                {
                    _selectedCoverSource = value; 
                    OnPropertyChanged(); 
                    UpdateActiveCoverFromSelection();
                }
            }
        }

        public bool HasMultipleCoverSources => _availableCovers.Count > 1;

        private void UpdateActiveCover()
        {
            UpdateAvailableCovers();
            if (UseLocalCover && LocalCover != null)
            {
                AlbumCover = LocalCover;
                CoverArtSource = "Local File";
                UpdateCoverResolution(LocalCover);
                var localSourceName = _availableCovers.FirstOrDefault(c => c.Name == "Local")?.Name ?? "";
                if (_selectedCoverSource != localSourceName)
                {
                    _selectedCoverSource = localSourceName;
                    OnPropertyChanged(nameof(SelectedCoverSource));
                }
            }
            else if (OnlineCover != null)
            {
                AlbumCover = OnlineCover;
                CoverArtSource = "Online Source";
                UpdateCoverResolution(OnlineCover);
                var onlineSourceName = _availableCovers.FirstOrDefault(c => c.Name != "Local")?.Name ?? "";
                if (_selectedCoverSource != onlineSourceName)
                {
                    _selectedCoverSource = onlineSourceName;
                    OnPropertyChanged(nameof(SelectedCoverSource));
                }
            }
            else
            {
                AlbumCover = null;
                CoverArtSource = "";
                CoverResolution = "";
                if (_selectedCoverSource != "")
                {
                    _selectedCoverSource = "";
                    OnPropertyChanged(nameof(SelectedCoverSource));
                }
            }
        }

        private void UpdateActiveCoverFromSelection()
        {
            var selectedCover = _availableCovers.FirstOrDefault(c => c.Name == _selectedCoverSource);
            if (selectedCover?.Image != null)
            {
                AlbumCover = selectedCover.Image;
                CoverArtSource = selectedCover.Source;
                UpdateCoverResolution(selectedCover.Image);
            }
        }

        private void UpdateAvailableCovers()
        {
            _availableCovers.Clear();
            
            if (LocalCover != null)
            {
                _availableCovers.Add(new CoverSource
                {
                    Name = "Local",
                    Image = LocalCover,
                    Source = "Local File"
                });
            }
            
            if (OnlineCover != null)
            {
                _availableCovers.Add(new CoverSource
                {
                    Name = "Online",
                    Image = OnlineCover,
                    Source = "Online Source"
                });
            }
            
            OnPropertyChanged(nameof(HasMultipleCoverSources));
        }

        public void AddOnlineCover(string sourceName, BitmapImage coverImage, string sourceDescription)
        {
            // Remove existing online covers (non-local) and add the new one
            var localCover = _availableCovers.FirstOrDefault(c => c.Name == "Local");
            _availableCovers.Clear();
            
            if (localCover != null)
            {
                _availableCovers.Add(localCover);
            }
            
            // Add the new online cover
            _availableCovers.Add(new CoverSource
            {
                Name = sourceName,
                Image = coverImage,
                Source = sourceDescription
            });
            
            OnPropertyChanged(nameof(HasMultipleCoverSources));
            OnPropertyChanged(nameof(AvailableCovers));
            
            // Update OnlineCover for backward compatibility and set as active if no local cover is selected
            OnlineCover = coverImage;
            if (LocalCover == null || !UseLocalCover)
            {
                if (_selectedCoverSource != sourceName)
                {
                    _selectedCoverSource = sourceName;
                    OnPropertyChanged(nameof(SelectedCoverSource));
                    UpdateActiveCoverFromSelection();
                }
            }
        }

        private void UpdateCoverResolution(BitmapImage? image)
        {
            if (image != null)
            {
                CoverResolution = $"{image.PixelWidth} × {image.PixelHeight}";
            }
            else
            {
                CoverResolution = "";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class MusicBrainzRelease
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Date { get; set; } = "";
        public int Score { get; set; }
        public int TrackCount { get; set; } = 0;
        public List<MusicBrainzTrack> Tracks { get; set; } = new List<MusicBrainzTrack>();
        public string? CoverArtUrl { get; set; }
        public BitmapImage? CoverArtImage { get; set; }
    }

    public class MusicBrainzTrack
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public int Position { get; set; }
        public int Length { get; set; }
    }

    public class DiscogsRelease
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Year { get; set; } = "";
        public string Genre { get; set; } = "";
        public int TrackCount { get; set; } = 0;
        public List<DiscogsTrack> Tracklist { get; set; } = new List<DiscogsTrack>();
        public string? CoverArtUrl { get; set; }
        public BitmapImage? CoverArtImage { get; set; }
    }

    public class DiscogsTrack
    {
        public string Position { get; set; } = "";
        public string Title { get; set; } = "";
        public string Duration { get; set; } = "";
    }

    public class OnlineSourceItem
    {
        public string DisplayName { get; set; } = "";
        public object Source { get; set; } = null!;
        public string SourceType { get; set; } = ""; // "MusicBrainz", "Discogs", "Fingerprint", etc.
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Score { get; set; } = "";
        public string AdditionalInfo { get; set; } = "";
        public object? Data { get; set; }
    }

    // Hierarchical data structure for album grouping
    public abstract class HierarchicalItem : INotifyPropertyChanged
    {
        private bool _isExpanded = true;
        private bool _isSelected = false;

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class AlbumGroup : HierarchicalItem
    {
        public string Album { get; set; } = "";
        public string Artist { get; set; } = "";
        public uint Year { get; set; }
        public string Genre { get; set; } = "";
        public ObservableCollection<AudioFileInfo> Tracks { get; set; } = new ObservableCollection<AudioFileInfo>();
        
        public string DisplayName => string.IsNullOrEmpty(Album) ? 
            $"Unknown Album ({Tracks.Count} tracks)" : 
            $"{Album}{(Year > 0 ? $" ({Year})" : "")} - {Artist} ({Tracks.Count} tracks)";
        
        public string AlbumInfo => string.IsNullOrEmpty(Album) ? "Unknown Album" : Album;
        public string ArtistInfo => string.IsNullOrEmpty(Artist) ? "Unknown Artist" : Artist;
        public string TrackCount => $"{Tracks.Count} track{(Tracks.Count != 1 ? "s" : "")}";
        
        public AlbumGroup()
        {
            Tracks.CollectionChanged += (s, e) => OnPropertyChanged(nameof(DisplayName));
            Tracks.CollectionChanged += (s, e) => OnPropertyChanged(nameof(TrackCount));
        }
    }

    public class HierarchicalDataItem : HierarchicalItem
    {
        public object? Item { get; set; }
        public bool IsAlbumGroup => Item is AlbumGroup;
        public bool IsTrack => Item is AudioFileInfo;
        public AlbumGroup? AsAlbumGroup => Item as AlbumGroup;
        public AudioFileInfo? AsTrack => Item as AudioFileInfo;
    }

    public class FileOnlineSourceCache
    {
        public string FilePath { get; set; } = "";
        public List<OnlineSourceItem> OnlineSourceResults { get; set; } = new List<OnlineSourceItem>();
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    public class OnlineSourceCacheManager
    {
        private readonly Dictionary<string, FileOnlineSourceCache> _cache = new();

        public void StoreResults(string filePath, List<OnlineSourceItem> results)
        {
            _cache[filePath] = new FileOnlineSourceCache
            {
                FilePath = filePath,
                OnlineSourceResults = results.ToList(),
                LastUpdated = DateTime.Now
            };
        }

        public List<OnlineSourceItem> GetResults(string filePath)
        {
            return _cache.TryGetValue(filePath, out var cache) ? 
                cache.OnlineSourceResults.ToList() : 
                new List<OnlineSourceItem>();
        }

        public bool HasCachedResults(string filePath)
        {
            return _cache.ContainsKey(filePath) && _cache[filePath].OnlineSourceResults.Count > 0;
        }

        public void ClearResultsForFile(string filePath)
        {
            _cache.Remove(filePath);
        }

        public void ClearAllResults()
        {
            _cache.Clear();
        }

        public IEnumerable<string> GetCachedFilePaths()
        {
            return _cache.Keys.ToList();
        }

        public int GetTotalCachedCount()
        {
            return _cache.Values.Sum(cache => cache.OnlineSourceResults.Count);
        }
    }
}