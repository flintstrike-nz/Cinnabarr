import React from 'react';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import Movie from './Movie';
import MoviePoster from './MoviePoster';
import { useUpcomingMovies } from './useWantedMovies';
import styles from './Movie.module.css';

function formatDate(date?: string) {
  return date ? date.slice(0, 10) : '';
}

function getReleases(movie: Movie) {
  return [
    { label: translate('InCinemas'), date: movie.inCinemas },
    { label: translate('DigitalRelease'), date: movie.digitalRelease },
    { label: translate('PhysicalRelease'), date: movie.physicalRelease },
  ].filter((release) => release.date);
}

function MovieCalendar() {
  const { data, isLoading, error } = useUpcomingMovies(90);

  return (
    <PageContent title={translate('MovieCalendar')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Calendar')}
          title={translate('Movies')}
        />

        {isLoading ? <LoadingIndicator /> : null}

        {error ? <p>{getErrorMessage(error)}</p> : null}

        {!isLoading && !error && !data.length ? (
          <p>{translate('NoResultsFound')}</p>
        ) : null}

        {data.length ? (
          <table className={styles.table}>
            <tbody>
              {data.map((movie) => (
                <tr key={movie.id}>
                  <td>
                    <MoviePoster movie={movie} small={true} />
                  </td>
                  <td>
                    <Link to={`/movie/${movie.id}`}>{movie.title}</Link> (
                    {movie.year})
                  </td>
                  <td>
                    {getReleases(movie).map((release) => (
                      <div key={release.label}>
                        {release.label}: {formatDate(release.date)}
                      </div>
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default MovieCalendar;
