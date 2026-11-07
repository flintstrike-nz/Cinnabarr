import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { kinds } from 'Helpers/Props';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import MoviePoster from './MoviePoster';
import { useHasMovies } from './useMovies';
import {
  useSearchWantedMovies,
  useWantedMovies,
  WantedKind,
} from './useWantedMovies';
import styles from './Movie.module.css';

interface WantedSectionProps {
  kind: WantedKind;
  title: string;
}

function WantedSection({ kind, title }: WantedSectionProps) {
  const { data, isLoading, error, totalRecords } = useWantedMovies(kind);
  const { isSearching, searchWanted } = useSearchWantedMovies();

  const handleSearchAllPress = useCallback(() => {
    searchWanted(kind);
  }, [searchWanted, kind]);

  return (
    <section>
      <h2>
        {title} ({totalRecords})
      </h2>

      {data.length ? (
        <div className={styles.actions}>
          <Button
            kind={kinds.PRIMARY}
            isDisabled={isSearching}
            onPress={handleSearchAllPress}
          >
            {translate('SearchAll')}
          </Button>
        </div>
      ) : null}

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
                  <Link to={`/movie/${movie.id}`}>{movie.title}</Link>
                </td>
                <td>{movie.year}</td>
                <td>
                  {movie.isAvailable
                    ? translate('Available')
                    : translate('NotAvailable')}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </section>
  );
}

// The movies still wanted, shown under the episodes on the Wanted pages. Nothing is shown without movies in the library.
export function WantedMoviesSection({ kind }: { kind: WantedKind }) {
  const hasMovies = useHasMovies();

  if (!hasMovies) {
    return null;
  }

  return (
    <WantedSection
      kind={kind}
      title={`${translate('Movies')}: ${
        kind === 'missing' ? translate('Missing') : translate('CutoffUnmet')
      }`}
    />
  );
}

function WantedMovies() {
  return (
    <PageContent title={translate('WantedMovies')}>
      <PageContentBody>
        <PageHeading scope={translate('Wanted')} title={translate('Movies')} />

        <WantedSection kind="missing" title={translate('Missing')} />

        <WantedSection kind="cutoff" title={translate('CutoffUnmet')} />
      </PageContentBody>
    </PageContent>
  );
}

export default WantedMovies;
