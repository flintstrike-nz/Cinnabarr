using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace Sonarr.Http.Frontend.Mappers
{
    // The TV launcher (tv.html): a plain page, not the single page app, so it runs in an old TV browser.
    public class TvHtmlMapper : HtmlMapperBase
    {
        private readonly IAppFolderInfo _appFolderInfo;

        public TvHtmlMapper(IAppFolderInfo appFolderInfo,
                            IDiskProvider diskProvider,
                            Lazy<ICacheBreakerProvider> cacheBreakProviderFactory,
                            IConfigFileProvider configFileProvider,
                            Logger logger)
            : base(diskProvider, configFileProvider, cacheBreakProviderFactory, logger)
        {
            _appFolderInfo = appFolderInfo;
        }

        protected override string FolderPath => _appFolderInfo.GetUiFolder();
        protected override string HtmlPath => Path.Combine(FolderPath, "tv.html");

        protected override string MapPath(string resourceUrl)
        {
            return HtmlPath;
        }

        public override bool CanHandle(string resourceUrl)
        {
            return resourceUrl.Equals("/tv", StringComparison.OrdinalIgnoreCase) ||
                   resourceUrl.StartsWith("/tv/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
