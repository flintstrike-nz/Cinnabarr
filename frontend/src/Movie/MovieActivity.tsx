import React from 'react';
import { Link } from 'react-router-dom';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import { getEventLabel, MovieHistoryItem } from './MovieHistory';
import styles from './Movie.module.css';

const NO_HISTORY: MovieHistoryItem[] = [];

function MovieActivity() {
  const {
    data = NO_HISTORY,
    isLoading,
    error,
  } = useApiQuery<MovieHistoryItem[]>({ path: '/history/movie' });

  return (
    <PageContent title={translate('MovieActivity')}>
      <PageContentBody>
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
                  </td>
                  <td>{item.sourceTitle}</td>
                  <td>{item.quality.quality.name}</td>
                  <td>{new Date(item.date).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default MovieActivity;
