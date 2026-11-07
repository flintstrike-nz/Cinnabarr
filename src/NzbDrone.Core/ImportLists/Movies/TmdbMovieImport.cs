using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.ImportLists.Movies
{
    // What is trending or popular on TMDb, through the metadata service.
    public class TmdbMovieImport : MovieImportListBase<TmdbMovieSettings>
    {
        private readonly IProvideMovieInfo _movieInfo;

        public TmdbMovieImport(IProvideMovieInfo movieInfo,
                               IImportListStatusService importListStatusService,
                               IConfigService configService,
                               IParsingService parsingService,
                               ILocalizationService localizationService,
                               Logger logger)
            : base(importListStatusService, configService, parsingService, localizationService, logger)
        {
            _movieInfo = movieInfo;
        }

        public override string Name => _localizationService.GetLocalizedString("ImportListsTmdbMovieSettingsName");

        public override ImportListType ListType => ImportListType.Tmdb;

        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(12);

        protected override IEnumerable<ImportListMovieInfo> FetchMovies()
        {
            var movies = Settings.ListType == (int)TmdbMovieListType.Popular
                ? _movieInfo.GetPopularMovies()
                : _movieInfo.GetTrendingMovies();

            return movies.Select(m => new ImportListMovieInfo
            {
                Title = m.Title,
                Year = m.Year,
                TmdbId = m.TmdbId,
                ImdbId = m.ImdbId
            }).ToList();
        }
    }
}
