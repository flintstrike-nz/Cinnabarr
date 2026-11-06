import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { QualityModel } from 'Quality/Quality';
import translate from 'Utilities/String/translate';
import styles from './Movie.module.css';

interface MovieHistoryItem {
  id: number;
  sourceTitle: string;
  quality: QualityModel;
  date: string;
  eventType: string;
  data: Record<string, string>;
}

interface MovieBlocklistItem {
  id: number;
  sourceTitle: string;
  quality: QualityModel;
  date: string;
  indexer?: string;
  message?: string;
}

const NO_HISTORY: MovieHistoryItem[] = [];
const NO_BLOCKLIST: MovieBlocklistItem[] = [];

function getEventLabel(eventType: string) {
  switch (eventType) {
    case 'grabbed':
      return translate('Grabbed');
    case 'downloadFolderImported':
      return translate('Imported');
    case 'downloadFailed':
      return translate('DownloadFailed');
    case 'movieFileDeleted':
      return translate('MovieFileDeleted');
    case 'movieFileRenamed':
      return translate('Renamed');
    case 'downloadIgnored':
      return translate('DownloadIgnored');
    default:
      return eventType;
  }
}

function BlocklistRow({
  item,
  movieId,
}: {
  item: MovieBlocklistItem;
  movieId: number;
}) {
  const queryClient = useQueryClient();
  const { mutate, isPending } = useApiMutation<unknown, void>({
    path: `/blocklist/movie/${item.id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({
          queryKey: ['/blocklist/movie', { movieId }],
        });
      },
    },
  });

  const handleRemove = useCallback(() => {
    mutate();
  }, [mutate]);

  return (
    <tr>
      <td>{item.sourceTitle}</td>
      <td>{item.indexer}</td>
      <td>{item.message}</td>
      <td>{new Date(item.date).toLocaleString()}</td>
      <td>
        <Button isDisabled={isPending} onPress={handleRemove}>
          {translate('Remove')}
        </Button>
      </td>
    </tr>
  );
}

function MovieHistory({ movieId }: { movieId: number }) {
  const { data: history = NO_HISTORY } = useApiQuery<MovieHistoryItem[]>({
    path: '/history/movie',
    queryParams: { movieId },
  });

  const { data: blocklist = NO_BLOCKLIST } = useApiQuery<MovieBlocklistItem[]>({
    path: '/blocklist/movie',
    queryParams: { movieId },
  });

  return (
    <>
      <h3>{translate('History')}</h3>

      {history.length ? (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>{translate('EventType')}</th>
              <th>{translate('SourceTitle')}</th>
              <th>{translate('Quality')}</th>
              <th>{translate('Date')}</th>
            </tr>
          </thead>
          <tbody>
            {history.map((item) => (
              <tr key={item.id}>
                <td>{getEventLabel(item.eventType)}</td>
                <td>{item.sourceTitle}</td>
                <td>{item.quality.quality.name}</td>
                <td>{new Date(item.date).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p>{translate('NoHistory')}</p>
      )}

      {blocklist.length ? (
        <>
          <h3>{translate('Blocklist')}</h3>

          <table className={styles.table}>
            <thead>
              <tr>
                <th>{translate('SourceTitle')}</th>
                <th>{translate('Indexer')}</th>
                <th>{translate('Message')}</th>
                <th>{translate('Date')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {blocklist.map((item) => (
                <BlocklistRow key={item.id} item={item} movieId={movieId} />
              ))}
            </tbody>
          </table>
        </>
      ) : null}
    </>
  );
}

export default MovieHistory;
