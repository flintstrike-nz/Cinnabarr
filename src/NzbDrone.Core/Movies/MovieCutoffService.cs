using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Movies
{
    public interface IMovieCutoffService
    {
        PagingSpec<Movie> MoviesWhereCutoffUnmet(PagingSpec<Movie> pagingSpec);
    }

    public class MovieCutoffService : IMovieCutoffService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IQualityProfileService _qualityProfileService;

        public MovieCutoffService(IMovieRepository movieRepository, IQualityProfileService qualityProfileService)
        {
            _movieRepository = movieRepository;
            _qualityProfileService = qualityProfileService;
        }

        public PagingSpec<Movie> MoviesWhereCutoffUnmet(PagingSpec<Movie> pagingSpec)
        {
            var qualitiesBelowCutoff = new List<QualitiesBelowCutoff>();

            // The qualities below the cutoff of each profile (or below the best allowed one when it can't be upgraded).
            foreach (var profile in _qualityProfileService.All())
            {
                var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirststAllowedQuality().Id;
                var cutoffIndex = profile.GetIndex(cutoff);
                var belowCutoff = profile.Items.Take(cutoffIndex.Index).ToList();

                if (belowCutoff.Any())
                {
                    qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, belowCutoff.SelectMany(i => i.GetQualities().Select(q => q.Id))));
                }
            }

            if (qualitiesBelowCutoff.Empty())
            {
                pagingSpec.Records = new List<Movie>();

                return pagingSpec;
            }

            return _movieRepository.MoviesWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff);
        }
    }
}
