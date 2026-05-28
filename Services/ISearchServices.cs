// ISearchServices.cs - Interfaces for the online metadata search services, so that
// view models can depend on abstractions and be tested with fakes.
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using TID3.Models;

namespace TID3.Services
{
    /// <summary>MusicBrainz release search and lookup.</summary>
    public interface IMusicBrainzService
    {
        Task<List<MusicBrainzRelease>> SearchReleases(string query);
        Task<MusicBrainzRelease?> GetReleaseDetails(string releaseId);
        Task<string?> GetCoverArtUrl(string releaseId);
        Task<BitmapImage?> LoadCoverArtImage(string? imageUrl);
        void RefreshSettings();
    }

    /// <summary>Discogs release search.</summary>
    public interface IDiscogsService
    {
        Task<List<DiscogsRelease>> SearchReleases(string query);
        void RefreshSettings();
    }
}
