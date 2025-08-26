// Models.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using TID3.Utils;

namespace TID3.Models
{
    public class CoverSource
    {
        public string Name { get; set; } = "";
        public BitmapImage? Image { get; set; }
        public string Source { get; set; } = "";
        public string Resolution 
        { 
            get 
            { 
                if (Image == null) return "";
                
                // Check if original dimensions are stored (for local files)
                var originalWidth = Image.GetValue(TID3.Services.TagService.OriginalWidthProperty);
                var originalHeight = Image.GetValue(TID3.Services.TagService.OriginalHeightProperty);
                
                TID3Logger.Debug("Images", "CoverSource resolution calculation", new {
                    OriginalWidth = originalWidth,
                    OriginalHeight = originalHeight,
                    CurrentWidth = Image.PixelWidth,
                    CurrentHeight = Image.PixelHeight
                }, "CoverSource");
                
                if (originalWidth is int width && originalHeight is int height && width > 0 && height > 0)
                {
                    TID3Logger.Debug("Images", "Using original dimensions", new { Width = width, Height = height }, "CoverSource");
                    return $"{width} × {height}";
                }
                
                // Fall back to current image dimensions (for online images)
                TID3Logger.Debug("Images", "Using current dimensions", new { 
                    Width = Image.PixelWidth, 
                    Height = Image.PixelHeight 
                }, "CoverSource");
                return $"{Image.PixelWidth} × {Image.PixelHeight}";
            } 
        }
        public CoverSourceType SourceType { get; set; } = CoverSourceType.Local;
        public string OriginalUrl { get; set; } = "";
    }

    public enum CoverSourceType
    {
        Local = 0,
        Spotify = 1,
        ITunes = 2,
        MusicBrainz = 3,
        LastFm = 4,
        Deezer = 5,
        Discogs = 6
    }

    public class CoverArtSourceSettings
    {
        public Dictionary<CoverSourceType, int> SourcePriorities { get; set; } = new()
        {
            { CoverSourceType.Local, 10 },      // Highest priority - user's files
            { CoverSourceType.Spotify, 8 },     // Official, high quality
            { CoverSourceType.ITunes, 7 },      // Official, good quality
            { CoverSourceType.MusicBrainz, 6 }, // Good database
            { CoverSourceType.Deezer, 5 },      // Decent quality
            { CoverSourceType.LastFm, 4 },      // User-submitted
            { CoverSourceType.Discogs, 3 }      // User-submitted
        };

        public bool EnableLastFm { get; set; } = true;
        public bool EnableSpotify { get; set; } = true;
        public bool EnableITunes { get; set; } = true;
        public bool EnableDeezer { get; set; } = true;
        public bool EnableMusicBrainz { get; set; } = true;
        public bool EnableDiscogs { get; set; } = true;

        // API Keys
        public string LastFmApiKey { get; set; } = "";
        public string SpotifyClientId { get; set; } = "";
        public string SpotifyClientSecret { get; set; } = "";

        public int GetPriority(CoverSourceType sourceType)
        {
            return SourcePriorities.TryGetValue(sourceType, out int priority) ? priority : 0;
        }

        public void SetPriority(CoverSourceType sourceType, int priority)
        {
            SourcePriorities[sourceType] = priority;
        }

        public bool IsSourceEnabled(CoverSourceType sourceType)
        {
            return sourceType switch
            {
                CoverSourceType.Local => true, // Always enabled
                CoverSourceType.LastFm => EnableLastFm,
                CoverSourceType.Spotify => EnableSpotify,
                CoverSourceType.ITunes => EnableITunes,
                CoverSourceType.Deezer => EnableDeezer,
                CoverSourceType.MusicBrainz => EnableMusicBrainz,
                CoverSourceType.Discogs => EnableDiscogs,
                _ => false
            };
        }
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
        private bool _isSelected = false;

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
            set { 
                TID3.Utils.TID3Logger.Debug("Images", "Setting AlbumCover property", new {
                    FileName = Path.GetFileName(FilePath),
                    HasValue = value != null
                }, "AudioFileInfo");
                if (value != null)
                {
                    TID3.Utils.TID3Logger.Images.LogImageDetails(value, "AlbumCover being set", "AudioFileInfo");
                }
                _albumCover = value; 
                OnPropertyChanged(); 
            }
        }

        public string CoverArtSource
        {
            get => _coverArtSource;
            set { _coverArtSource = value; OnPropertyChanged(); }
        }

        public BitmapImage? LocalCover
        {
            get => _localCover;
            set { 
                TID3.Utils.TID3Logger.Debug("Images", "Setting LocalCover property", new {
                    FileName = Path.GetFileName(FilePath),
                    HasValue = value != null
                }, "AudioFileInfo");
                if (value != null)
                {
                    TID3.Utils.TID3Logger.Images.LogImageDetails(value, "LocalCover being set", "AudioFileInfo");
                }
                _localCover = value; 
                OnPropertyChanged(); 
                UpdateActiveCover(); 
            }
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
            using var scope = TID3.Utils.TID3Logger.BeginScope("Images", "UpdateActiveCover", new {
                FileName = Path.GetFileName(FilePath),
                UseLocalCover = UseLocalCover,
                HasLocalCover = LocalCover != null,
                HasOnlineCover = OnlineCover != null
            }, "AudioFileInfo");
            
            UpdateAvailableCovers();
            if (UseLocalCover && LocalCover != null)
            {
                TID3.Utils.TID3Logger.Debug("Images", "Using LocalCover as AlbumCover", component: "AudioFileInfo");
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
            // Only update if there are no specific named covers already added by AddOnlineCover
            var hasSpecificSources = _availableCovers.Any(c => c.Name != "Local" && c.Name != "Online");
            
            if (!hasSpecificSources)
            {
                // Remove old generic online covers but keep local and specific sources
                var onlineCoversToRemove = _availableCovers.Where(c => c.Name == "Online").ToList();
                foreach (var cover in onlineCoversToRemove)
                {
                    _availableCovers.Remove(cover);
                }
                
                // Add local cover if not already present
                if (LocalCover != null && !_availableCovers.Any(c => c.Name == "Local"))
                {
                    _availableCovers.Add(new CoverSource
                    {
                        Name = "Local",
                        Image = LocalCover,
                        Source = "Local File"
                    });
                }
                
                // Add generic online cover only if no specific sources exist
                if (OnlineCover != null && !_availableCovers.Any(c => c.Name != "Local"))
                {
                    _availableCovers.Add(new CoverSource
                    {
                        Name = "Online",
                        Image = OnlineCover,
                        Source = "Online Source"
                    });
                }
            }
            
            OnPropertyChanged(nameof(HasMultipleCoverSources));
        }

        public void AddOnlineCover(string sourceName, BitmapImage coverImage, string sourceDescription)
        {
            // Check if this source already exists and remove it
            var existingSource = _availableCovers.FirstOrDefault(c => c.Name == sourceName);
            if (existingSource != null)
            {
                _availableCovers.Remove(existingSource);
            }
            
            // Add the new online cover
            var newCover = new CoverSource
            {
                Name = sourceName,
                Image = coverImage,
                Source = sourceDescription
            };
            _availableCovers.Add(newCover);
            
            OnPropertyChanged(nameof(HasMultipleCoverSources));
            OnPropertyChanged(nameof(AvailableCovers));
            
            // Update OnlineCover for backward compatibility 
            OnlineCover = coverImage;
            
            // Auto-select the first added cover if no cover is currently selected
            if (string.IsNullOrEmpty(_selectedCoverSource) || !_availableCovers.Any(c => c.Name == _selectedCoverSource))
            {
                _selectedCoverSource = sourceName;
                OnPropertyChanged(nameof(SelectedCoverSource));
                UpdateActiveCoverFromSelection();
            }
        }

        private void UpdateCoverResolution(BitmapImage? image)
        {
            if (image != null)
            {
                // Check if original dimensions are stored (for local files)
                var originalWidth = image.GetValue(TID3.Services.TagService.OriginalWidthProperty);
                var originalHeight = image.GetValue(TID3.Services.TagService.OriginalHeightProperty);
                
                if (originalWidth is int width && originalHeight is int height && width > 0 && height > 0)
                {
                    CoverResolution = $"{width} × {height}";
                }
                else
                {
                    // Fall back to current image dimensions (for online images)
                    CoverResolution = $"{image.PixelWidth} × {image.PixelHeight}";
                }
            }
            else
            {
                CoverResolution = "";
            }
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

    public class OnlineSourceItem : INotifyPropertyChanged
    {
        private string _displayName = "";
        private string _scoreCategory = "Unknown";
        
        public string DisplayName 
        { 
            get => _displayName;
            set 
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    _scoreCategory = CalculateScoreCategory();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ScoreCategory));
                }
            }
        }
        
        public object Source { get; set; } = null!;
        public string SourceType { get; set; } = ""; // "MusicBrainz", "Discogs", "Fingerprint", etc.
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Score { get; set; } = "";
        public string AdditionalInfo { get; set; } = "";
        public object? Data { get; set; }
        
        public string ScoreCategory 
        { 
            get 
            {
                // Ensure ScoreCategory is calculated if it hasn't been yet
                if (_scoreCategory == "Unknown" && !string.IsNullOrEmpty(_displayName))
                {
                    _scoreCategory = CalculateScoreCategory();
                    // Don't call OnPropertyChanged here to avoid recursion
                }
                return _scoreCategory;
            }
            private set 
            {
                if (_scoreCategory != value)
                {
                    _scoreCategory = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private string CalculateScoreCategory()
        {
            // Try multiple regex patterns to catch different formats, including comma decimals
            var patterns = new[]
            {
                @"\[(\d+[,.]?\d*)%\]",           // [85.2%] or [85,2%] - MusicBrainz/Discogs format
                @"\((\d+[,.]?\d*)%\)",           // (85.2%) or (85,2%) - Fingerprint format  
                @"(\d+[,.]?\d*)%",               // Just the percentage anywhere
            };
            
            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(_displayName, pattern);
                if (match.Success)
                {
                    // Handle both comma and dot decimal separators
                    string scoreText = match.Groups[1].Value.Replace(',', '.');
                    if (double.TryParse(scoreText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double score))
                    {
                        return score switch
                        {
                            >= 90 => "Excellent",  // 90%+ = Bright Green
                            >= 80 => "Great",      // 80-89% = Green  
                            >= 70 => "Good",       // 70-79% = Yellow-Green
                            >= 60 => "Fair",       // 60-69% = Orange
                            >= 50 => "Poor",       // 50-59% = Red-Orange
                            _ => "VeryPoor"        // <50% = Red
                        };
                    }
                }
            }
            
            return "Unknown"; // No score found - use default color
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
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
            Tracks.CollectionChanged += Tracks_CollectionChanged;
        }
        
        private void Tracks_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Use Dispatcher.BeginInvoke to avoid stack overflow from immediate property change notifications
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(TrackCount));
                }));
            }
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