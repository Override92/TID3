// Models.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace TID3
{
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