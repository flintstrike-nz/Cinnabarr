using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Movies
{
    /// <summary>Orders the decisions for one movie, best last (callers sort descending): quality, custom format score, protocol, indexer priority, peers or age, then size.</summary>
    public class MovieDownloadDecisionComparer : IComparer<DownloadDecision>
    {
        private readonly IConfigService _configService;
        private readonly IDelayProfileService _delayProfileService;

        public MovieDownloadDecisionComparer(IConfigService configService, IDelayProfileService delayProfileService)
        {
            _configService = configService;
            _delayProfileService = delayProfileService;
        }

        public int Compare(DownloadDecision x, DownloadDecision y)
        {
            var comparers = new List<Func<RemoteMovie, RemoteMovie, int>>
            {
                CompareQuality,
                CompareCustomFormatScore,
                CompareProtocol,
                CompareIndexerPriority,
                ComparePeersIfTorrent,
                CompareAgeIfUsenet,
                CompareSize
            };

            return comparers.Select(comparer => comparer(x.RemoteMovie, y.RemoteMovie)).FirstOrDefault(result => result != 0);
        }

        private static int CompareBy<TValue>(RemoteMovie left, RemoteMovie right, Func<RemoteMovie, TValue> funcValue)
            where TValue : IComparable<TValue>
        {
            return funcValue(left).CompareTo(funcValue(right));
        }

        private static int CompareByReverse<TValue>(RemoteMovie left, RemoteMovie right, Func<RemoteMovie, TValue> funcValue)
            where TValue : IComparable<TValue>
        {
            return CompareBy(left, right, funcValue) * -1;
        }

        private static int CompareAll(params int[] comparers)
        {
            return comparers.FirstOrDefault(result => result != 0);
        }

        private int CompareQuality(RemoteMovie x, RemoteMovie y)
        {
            var quality = CompareBy(x, y, r => r.Movie.QualityProfile.GetIndex(r.ParsedMovieInfo.Quality.Quality));

            if (_configService.DownloadPropersAndRepacks == ProperDownloadTypes.DoNotPrefer)
            {
                return quality;
            }

            return CompareAll(quality, CompareBy(x, y, r => r.ParsedMovieInfo.Quality.Revision));
        }

        private static int CompareCustomFormatScore(RemoteMovie x, RemoteMovie y)
        {
            return CompareBy(x, y, r => r.CustomFormatScore);
        }

        private int CompareProtocol(RemoteMovie x, RemoteMovie y)
        {
            return CompareBy(x, y, r =>
            {
                var delayProfile = _delayProfileService.BestForTags(r.Movie.Tags);

                return r.Release.DownloadProtocol == delayProfile.PreferredProtocol;
            });
        }

        private static int CompareIndexerPriority(RemoteMovie x, RemoteMovie y)
        {
            return CompareByReverse(x, y, r => r.Release.IndexerPriority);
        }

        private static int ComparePeersIfTorrent(RemoteMovie x, RemoteMovie y)
        {
            // Different protocols are settled by the preferred protocol, since all of these are for the same movie.
            if (x.Release.DownloadProtocol != DownloadProtocol.Torrent || y.Release.DownloadProtocol != DownloadProtocol.Torrent)
            {
                return 0;
            }

            return CompareAll(
                CompareBy(x, y, r =>
                {
                    var seeders = TorrentInfo.GetSeeders(r.Release);

                    return seeders.HasValue && seeders.Value > 0 ? Math.Round(Math.Log10(seeders.Value)) : 0;
                }),
                CompareBy(x, y, r =>
                {
                    var peers = TorrentInfo.GetPeers(r.Release);

                    return peers.HasValue && peers.Value > 0 ? Math.Round(Math.Log10(peers.Value)) : 0;
                }));
        }

        private static int CompareAgeIfUsenet(RemoteMovie x, RemoteMovie y)
        {
            if (x.Release.DownloadProtocol != DownloadProtocol.Usenet || y.Release.DownloadProtocol != DownloadProtocol.Usenet)
            {
                return 0;
            }

            return CompareBy(x, y, r =>
            {
                if (r.Release.AgeHours < 1)
                {
                    return 1000;
                }

                if (r.Release.AgeHours <= 24)
                {
                    return 100;
                }

                if (r.Release.Age <= 7)
                {
                    return 10;
                }

                return Math.Round(Math.Log10(r.Release.Age)) * -1;
            });
        }

        private static int CompareSize(RemoteMovie x, RemoteMovie y)
        {
            return CompareBy(x, y, r =>
            {
                var profile = r.Movie.QualityProfile;
                var index = profile.GetIndex(r.ParsedMovieInfo.Quality.Quality, true);
                var qualityOrGroup = profile.Items[index.Index];
                var item = qualityOrGroup.Quality == null ? qualityOrGroup.Items[index.GroupIndex] : qualityOrGroup;
                var runtime = r.Movie.MovieMetadata.Value.Runtime;

                // With no preferred size (unlimited) the largest is best; otherwise the closest to the preferred size.
                if (item.PreferredSize.HasValue && runtime > 0)
                {
                    var preferredReleaseSize = runtime * item.PreferredSize.Value.Megabytes();

                    return Math.Abs((r.Release.Size - preferredReleaseSize).Round(200.Megabytes())) * -1;
                }

                return r.Release.Size.Round(200.Megabytes());
            });
        }
    }
}
