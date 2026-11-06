import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import { QualityModel } from 'Quality/Quality';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import styles from './Movie.module.css';

interface MovieFileItem {
  id: number;
  relativePath: string;
  size: number;
  releaseGroup?: string;
  quality?: QualityModel;
  mediaInfo?: { videoCodec?: string; resolution?: string };
}

const NO_FILES: MovieFileItem[] = [];

function FileRow({ file, movieId }: { file: MovieFileItem; movieId: number }) {
  const queryClient = useQueryClient();
  const { mutate, isPending, error } = useApiMutation<unknown, void>({
    path: `/moviefile/${file.id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/moviefile'] });
        queryClient.invalidateQueries({ queryKey: [`/movie/${movieId}`] });
        queryClient.invalidateQueries({ queryKey: ['/movie'] });
      },
    },
  });

  const [isConfirmOpen, setIsConfirmOpen] = useState(false);

  const handleDeletePress = useCallback(() => {
    setIsConfirmOpen(true);
  }, []);

  const handleCancel = useCallback(() => {
    setIsConfirmOpen(false);
  }, []);

  const handleConfirm = useCallback(() => {
    setIsConfirmOpen(false);
    mutate();
  }, [mutate]);

  return (
    <tr>
      <td>
        {file.relativePath}
        {error ? <div>{getErrorMessage(error)}</div> : null}
      </td>
      <td>{file.quality?.quality.name}</td>
      <td>{file.mediaInfo?.resolution}</td>
      <td>{file.mediaInfo?.videoCodec}</td>
      <td>{file.releaseGroup}</td>
      <td>{formatBytes(file.size)}</td>
      <td>
        <Button
          kind={kinds.DANGER}
          isDisabled={isPending}
          onPress={handleDeletePress}
        >
          {translate('Delete')}
        </Button>

        <ConfirmModal
          isOpen={isConfirmOpen}
          kind={kinds.DANGER}
          title={translate('Delete')}
          message={translate('DeleteMovieFileConfirmation')}
          confirmLabel={translate('Delete')}
          onConfirm={handleConfirm}
          onCancel={handleCancel}
        />
      </td>
    </tr>
  );
}

function MovieFiles({ movieId }: { movieId: number }) {
  const { data: files = NO_FILES } = useApiQuery<MovieFileItem[]>({
    path: '/moviefile',
    queryParams: { movieId },
  });

  if (!files.length) {
    return null;
  }

  return (
    <>
      <h3>{translate('MovieFiles')}</h3>

      <table className={styles.table}>
        <thead>
          <tr>
            <th>{translate('Path')}</th>
            <th>{translate('Quality')}</th>
            <th>{translate('Resolution')}</th>
            <th>{translate('VideoCodec')}</th>
            <th>{translate('ReleaseGroup')}</th>
            <th>{translate('Size')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {files.map((file) => (
            <FileRow key={file.id} file={file} movieId={movieId} />
          ))}
        </tbody>
      </table>
    </>
  );
}

export default MovieFiles;
