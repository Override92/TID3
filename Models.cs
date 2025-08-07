// Models.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

        public string FilePath { get; set; } = "";
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public string Duration { get; set; } = "";
        public string Bitrate { get; set; } = "";
        public string FileSize { get; set; } = "";

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
}