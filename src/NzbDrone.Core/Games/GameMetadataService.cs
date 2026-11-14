using System;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Games
{
    public class GameMetadataService : IExecute<RefreshGameMetadataCommand>
    {
        private static readonly TimeSpan RetryAfter = TimeSpan.FromDays(30);

        private readonly IGameRepository _repository;
        private readonly IIgdbClient _igdb;
        private readonly Logger _logger;

        public GameMetadataService(IGameRepository repository, IIgdbClient igdb, Logger logger)
        {
            _repository = repository;
            _igdb = igdb;
            _logger = logger;
        }

        public void Execute(RefreshGameMetadataCommand message)
        {
            if (!_igdb.IsConfigured)
            {
                _logger.Debug("Games metadata: IGDB is not set up");
                return;
            }

            var due = _repository.All()
                                 .Where(g => g.IgdbId == null && (g.MetadataChecked == null || DateTime.UtcNow - g.MetadataChecked > RetryAfter))
                                 .ToList();
            var found = 0;

            foreach (var game in due)
            {
                try
                {
                    var match = IgdbMatcher.PickBest(game.Title, _igdb.Search(game.Title, game.PlatformKey));

                    if (match != null)
                    {
                        Apply(game, match);
                        found++;
                    }

                    game.MetadataChecked = DateTime.UtcNow;
                    _repository.Update(game);
                }
                catch (Exception ex)
                {
                    // Stop on the first failure (bad key, no network, rate limit); the rest are tried next time.
                    _logger.Warn(ex, "Games metadata: stopping after an IGDB error on {0}", game.Title);
                    break;
                }

                // IGDB allows about four requests a second.
                Thread.Sleep(300);
            }

            _logger.Info("Games metadata: {0} of {1} found", found, due.Count);
        }

        internal static void Apply(Game game, IgdbGame match)
        {
            game.IgdbId = match.Id;
            game.Overview = match.Summary;
            game.Year = match.FirstReleaseDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(match.FirstReleaseDate.Value).Year : (int?)null;
            game.Genres = match.Genres == null ? null : string.Join(", ", match.Genres.Select(g => g.Name));
            game.Developer = match.InvolvedCompanies?.FirstOrDefault(c => c.Developer)?.Company?.Name;
            game.Rating = match.Rating.HasValue ? Math.Round(match.Rating.Value) : (double?)null;
            game.CoverUrl = IgdbMatcher.CoverUrl(match);
        }
    }
}
