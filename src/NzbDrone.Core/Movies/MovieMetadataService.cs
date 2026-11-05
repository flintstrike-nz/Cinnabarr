using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Translations;

namespace NzbDrone.Core.Movies
{
    public interface IMovieMetadataService
    {
        MovieMetadata Get(int id);
        MovieMetadata FindByTmdbId(int tmdbId);
        MovieMetadata FindByImdbId(string imdbId);
        List<MovieMetadata> GetMoviesWithCollections();
        List<MovieMetadata> GetMoviesByCollectionTmdbId(int collectionId);
        bool Upsert(MovieMetadata movie);
        bool UpsertMany(List<MovieMetadata> movies);
        void DeleteMany(List<MovieMetadata> movies);
    }

    public class MovieMetadataService : IMovieMetadataService
    {
        private readonly IMovieMetadataRepository _movieMetadataRepository;
        private readonly ICreditRepository _creditRepository;
        private readonly IAlternativeTitleRepository _alternativeTitleRepository;
        private readonly IMovieTranslationRepository _translationRepository;

        public MovieMetadataService(IMovieMetadataRepository movieMetadataRepository,
                                    ICreditRepository creditRepository,
                                    IAlternativeTitleRepository alternativeTitleRepository,
                                    IMovieTranslationRepository translationRepository)
        {
            _movieMetadataRepository = movieMetadataRepository;
            _creditRepository = creditRepository;
            _alternativeTitleRepository = alternativeTitleRepository;
            _translationRepository = translationRepository;
        }

        public MovieMetadata FindByTmdbId(int tmdbId)
        {
            return _movieMetadataRepository.FindByTmdbId(tmdbId);
        }

        public MovieMetadata FindByImdbId(string imdbId)
        {
            return _movieMetadataRepository.FindByImdbId(imdbId);
        }

        public List<MovieMetadata> GetMoviesWithCollections()
        {
            return _movieMetadataRepository.GetMoviesWithCollections();
        }

        public List<MovieMetadata> GetMoviesByCollectionTmdbId(int collectionId)
        {
            return _movieMetadataRepository.GetMoviesByCollectionTmdbId(collectionId);
        }

        public MovieMetadata Get(int id)
        {
            return _movieMetadataRepository.Get(id);
        }

        public bool Upsert(MovieMetadata movie)
        {
            return _movieMetadataRepository.UpsertMany(new List<MovieMetadata> { movie });
        }

        public bool UpsertMany(List<MovieMetadata> movies)
        {
            return _movieMetadataRepository.UpsertMany(movies);
        }

        public void DeleteMany(List<MovieMetadata> movies)
        {
            // Whoever deletes metadata checks first that nothing still uses it (movies, lists).
            var ids = movies.Select(m => m.Id).ToList();

            _creditRepository.DeleteForMovies(ids);
            _alternativeTitleRepository.DeleteForMovies(ids);
            _translationRepository.DeleteForMovies(ids);

            _movieMetadataRepository.DeleteMany(ids);
        }
    }
}
