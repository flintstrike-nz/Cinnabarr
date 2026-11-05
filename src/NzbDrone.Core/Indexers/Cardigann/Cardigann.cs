using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Engine;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;
using EngineBasicCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.BasicSearchCriteria;
using EngineCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.SearchCriteriaBase;
using EngineTvCriteria = NzbDrone.Core.IndexerEngine.IndexerSearch.Definitions.TvSearchCriteria;

namespace NzbDrone.Core.Indexers.Cardigann
{
    /// <summary>
    /// An indexer described by a definition file. The indexer engine does the logging in, searching and
    /// parsing; this class translates Embarr's searches and releases to and from the engine.
    /// </summary>
    public class Cardigann : IndexerBase<CardigannSettings>, IIndexerFileDownloader
    {
        // 5000 is the standard TV category and 5070 is anime; the definition maps them to the site's own.
        private static readonly int[] TvCategories = { 5000 };
        private static readonly int[] AnimeCategories = { 5070 };

        private readonly IEngineIndexerProvider _engineProvider;

        public Cardigann(IEngineIndexerProvider engineProvider,
                         IIndexerStatusService indexerStatusService,
                         IConfigService configService,
                         IParsingService parsingService,
                         Logger logger,
                         ILocalizationService localizationService)
            : base(indexerStatusService, configService, parsingService, logger, localizationService)
        {
            _engineProvider = engineProvider;
        }

        public override string Name => "Cardigann";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override bool SupportsRss => true;
        public override bool SupportsSearch => true;
        public override IEnumerable<FailDownloads> DefaultFailDownloads => [FailDownloads.Executables, FailDownloads.PotentiallyDangerous];

        // Indexers are added from the list of definitions (Settings > Indexers), not from this generic entry.
        public override IEnumerable<ProviderDefinition> DefaultDefinitions => Array.Empty<ProviderDefinition>();

        private EngineSearchOptions Options => new()
        {
            Categories = TvCategories,
            AnimeCategories = AnimeCategories,
            AnimeStandardFormatSearch = true
        };

        public override Task<IList<ReleaseInfo>> FetchRecent()
        {
            return Run(caps => EngineSearchCriteriaMapper.Recent(caps, Options));
        }

        public override Task<IList<ReleaseInfo>> Fetch(SeasonSearchCriteria searchCriteria)
        {
            return Run(caps => EngineSearchCriteriaMapper.Map(searchCriteria, caps, Options));
        }

        public override Task<IList<ReleaseInfo>> Fetch(SingleEpisodeSearchCriteria searchCriteria)
        {
            return Run(caps => EngineSearchCriteriaMapper.Map(searchCriteria, caps, Options));
        }

        public override Task<IList<ReleaseInfo>> Fetch(DailyEpisodeSearchCriteria searchCriteria)
        {
            return Run(caps => EngineSearchCriteriaMapper.Map(searchCriteria, caps, Options));
        }

        public override Task<IList<ReleaseInfo>> Fetch(DailySeasonSearchCriteria searchCriteria)
        {
            return Run(caps => EngineSearchCriteriaMapper.Map(searchCriteria, caps, Options));
        }

        public override Task<IList<ReleaseInfo>> Fetch(AnimeEpisodeSearchCriteria searchCriteria)
        {
            return Run(caps => EngineSearchCriteriaMapper.Map(searchCriteria, caps, Options));
        }

        public override Task<IList<ReleaseInfo>> Fetch(AnimeSeasonSearchCriteria searchCriteria)
        {
            return Run(caps => EngineSearchCriteriaMapper.Map(searchCriteria, caps, Options));
        }

        public override Task<IList<ReleaseInfo>> Fetch(SpecialEpisodeSearchCriteria searchCriteria)
        {
            return Run(caps => EngineSearchCriteriaMapper.Map(searchCriteria, caps, Options));
        }

        public override HttpRequest GetDownloadRequest(string link)
        {
            return new HttpRequest(link);
        }

        public async Task<byte[]> DownloadTorrentFile(string link)
        {
            var response = await _engineProvider.Get((IndexerDefinition)Definition).Download(new Uri(link));

            return response.Data;
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            return _engineProvider.Get((IndexerDefinition)Definition).RequestAction(action, query);
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            var engineIndexer = _engineProvider.Get((IndexerDefinition)Definition);

            // The engine's test is synchronous and makes web requests, so keep it off the caller's thread.
            var result = await Task.Run(() => engineIndexer.Test());

            failures.AddRange(result.Errors);
        }

        private async Task<IList<ReleaseInfo>> Run(Func<IndexerEngine.Indexers.IndexerCapabilities, List<List<EngineCriteria>>> buildTiers)
        {
            var engineIndexer = _engineProvider.Get((IndexerDefinition)Definition);
            var tiers = buildTiers(engineIndexer.GetCapabilities());
            var releases = new List<ReleaseInfo>();

            foreach (var tier in tiers)
            {
                foreach (var criteria in tier)
                {
                    var result = criteria switch
                    {
                        EngineTvCriteria tv => await engineIndexer.Fetch(tv),
                        EngineBasicCriteria basic => await engineIndexer.Fetch(basic),
                        _ => null
                    };

                    if (result != null)
                    {
                        releases.AddRange(result.Releases.Select(EngineReleaseMapper.Map));
                    }
                }

                // Later tiers are fallbacks; stop at the first one that found something.
                if (releases.Any())
                {
                    break;
                }
            }

            return CleanupReleases(releases);
        }
    }
}
