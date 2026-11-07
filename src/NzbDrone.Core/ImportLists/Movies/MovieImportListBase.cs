using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.ImportLists.Movies
{
    // Base for lists of movies. They return no series; the movies travel in ImportListFetchResult.Movies.
    public abstract class MovieImportListBase<TSettings> : ImportListBase<TSettings>
        where TSettings : IImportListSettings, new()
    {
        protected MovieImportListBase(IImportListStatusService importListStatusService,
                                      IConfigService configService,
                                      IParsingService parsingService,
                                      ILocalizationService localizationService,
                                      Logger logger)
            : base(importListStatusService, configService, parsingService, localizationService, logger)
        {
        }

        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        protected abstract IEnumerable<ImportListMovieInfo> FetchMovies();

        public override ImportListFetchResult Fetch()
        {
            var movies = new List<ImportListMovieInfo>();
            var anyFailure = false;

            try
            {
                movies.AddRange(FetchMovies());

                _importListStatusService.RecordSuccess(Definition.Id);
            }
            catch (Exception ex)
            {
                anyFailure = true;
                _logger.Debug(ex, "Failed to fetch data for list {0} ({1})", Definition.Name, Name);

                _importListStatusService.RecordFailure(Definition.Id);
            }

            var result = new ImportListFetchResult { AnyFailure = anyFailure };

            result.Movies.AddRange(movies.Where(m => m.TmdbId > 0 || !string.IsNullOrWhiteSpace(m.ImdbId))
                                         .DistinctBy(m => new { m.TmdbId, m.ImdbId })
                                         .Select(m =>
                                         {
                                             m.ImportListId = Definition.Id;
                                             m.ImportList = Definition.Name;
                                             return m;
                                         }));

            return result;
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                FetchMovies().ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to read the movie list");
                failures.Add(new ValidationFailure(string.Empty, _localizationService.GetLocalizedString("ImportListsValidationTestFailed", new Dictionary<string, object> { { "exceptionMessage", ex.Message } })));
            }
        }
    }
}
