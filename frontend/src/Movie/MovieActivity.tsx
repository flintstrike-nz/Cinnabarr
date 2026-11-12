import React from 'react';
import { Link } from 'react-router-dom';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  BlocklistRow,
  getEventLabel,
  MovieBlocklistItem,
  MovieHistoryItem,
} from './MovieHistory';
import { useHasMovies } from './useMovies';
import styles from './Movie.module.css';

const NO_HISTORY: MovieHistoryItem[] = [];
const NO_BLOCKLIST: MovieBlocklistItem[] = [];

// The latest movie events, shown under the episodes' history. Nothing is shown without movies in the library.
export function MovieHistorySection() {
  const hasMovies = useHasMovies();
  const {
    data = NO_HISTORY,
    isLoading,
    error,
  } = useApiQuery<MovieHistoryItem[]>({
    path: '/history/movie',
    queryOptions: { enabled: hasMovies },
  });

  if (!hasMovies) {
    return null;
  }

  return (
    <section>
      <h2>{translate('Movies')}</h2>

      {isLoading ? <LoadingIndicator /> : null}

      {error ? <p>{getErrorMessage(error)}</p> : null}

      {!isLoading && !error && !data.length ? (
        <p>{translate('NoHistory')}</p>
      ) : null}

      {data.length ? (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>{translate('EventType')}</th>
              <th>{translate('Movie')}</th>
              <th>{translate('SourceTitle')}</th>
              <th>{translate('Quality')}</th>
              <th>{translate('Date')}</th>
            </tr>
          </thead>
          <tbody>
            {data.map((item) => (
              <tr key={item.id}>
                <td>{getEventLabel(item.eventType)}</td>
                <td>
                  <Link to={`/movie/${item.movieId}`}>{item.movieTitle}</Link>
                  {item.versionLabel ? ` · ${item.versionLabel}` : null}
                </td>
                <td>{item.sourceTitle}</td>
                <td>{item.quality.quality.name}</td>
                <td>{new Date(item.date).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </section>
  );
}

// Every blocklisted movie release, shown under the episodes' blocklist.
export function MovieBlocklistSection() {
  const hasMovies = useHasMovies();
  const { data = NO_BLOCKLIST, isLoading } = useApiQuery<MovieBlocklistItem[]>({
    path: '/blocklist/movie',
    queryOptions: { enabled: hasMovies },
  });

  if (!hasMovies) {
    return null;
  }

  return (
    <section>
      <h2>{translate('Movies')}</h2>

      {isLoading ? <LoadingIndicator /> : null}

      {!isLoading && !data.length ? (
        <p>{translate('NoBlocklistItems')}</p>
      ) : null}

      {data.length ? (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>{translate('Movie')}</th>
              <th>{translate('SourceTitle')}</th>
              <th>{translate('Indexer')}</th>
              <th>{translate('Message')}</th>
              <th>{translate('Date')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {data.map((item) => (
              <BlocklistRow key={item.id} item={item} showMovie={true} />
            ))}
          </tbody>
        </table>
      ) : null}
    </section>
  );
}
