import React from 'react';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { icons, kinds } from 'Helpers/Props';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import MoviePoster from './MoviePoster';
import { useMovies } from './useMovies';
import styles from './Movie.module.css';

function MoviesIndex() {
  const { data: movies, isLoading, error } = useMovies();
  const profiles = useQualityProfilesData();

  return (
    <PageContent title={translate('Movies')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Media')}
          title={translate('Movies')}
          actions={
            <Button to="/add/movie" kind={kinds.PRIMARY}>
              {translate('AddMovie')}
            </Button>
          }
        />

        {isLoading ? <LoadingIndicator /> : null}

        {error ? <div>{getErrorMessage(error)}</div> : null}

        {!isLoading && !error && !movies.length ? (
          <div>
            <p>{translate('NoMoviesHaveBeenAdded')}</p>
            <Button to="/add/movie" kind={kinds.PRIMARY}>
              {translate('AddMovie')}
            </Button>
          </div>
        ) : null}

        {movies.length ? (
          <table className={styles.table}>
            <thead>
              <tr>
                <th />
                <th>{translate('Title')}</th>
                <th>{translate('Year')}</th>
                <th>{translate('Status')}</th>
                <th>{translate('QualityProfile')}</th>
                <th>{translate('Size')}</th>
              </tr>
            </thead>
            <tbody>
              {[...movies]
                .sort((a, b) => a.title.localeCompare(b.title))
                .map((movie) => (
                  <tr key={movie.id}>
                    <td>
                      <MoviePoster movie={movie} small={true} />
                    </td>
                    <td>
                      <Icon
                        name={
                          movie.monitored ? icons.MONITORED : icons.UNMONITORED
                        }
                        title={
                          movie.monitored
                            ? translate('Monitored')
                            : translate('Unmonitored')
                        }
                      />{' '}
                      <Link to={`/movie/${movie.id}`}>{movie.title}</Link>
                    </td>
                    <td>{movie.year}</td>
                    <td>
                      {movie.hasFile
                        ? translate('Downloaded')
                        : translate('Missing')}
                    </td>
                    <td>
                      {profiles.find((p) => p.id === movie.qualityProfileId)
                        ?.name ?? ''}
                    </td>
                    <td>
                      {movie.hasFile ? formatBytes(movie.sizeOnDisk) : ''}
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

export default MoviesIndex;
