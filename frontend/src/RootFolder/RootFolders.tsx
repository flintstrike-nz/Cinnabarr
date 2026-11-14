import React from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import RootFolderRow from './RootFolderRow';
import useRootFolders from './useRootFolders';
import styles from './RootFolders.module.css';

interface RootFoldersProps {
  isSelectable?: boolean;

  // Only the root folders that can hold this kind of library are listed.
  library?: 'series' | 'movies';
}

function RootFolders({ isSelectable = false, library }: RootFoldersProps) {
  const { isFetching, isFetched, error, data: all } = useRootFolders();
  const data = library
    ? all.filter((r) => r.library === 'any' || r.library === library)
    : all;

  if (isFetching && !isFetched) {
    return <LoadingIndicator />;
  }

  if (!isFetching && !!error) {
    return (
      <Alert kind={kinds.DANGER}>{translate('RootFoldersLoadError')}</Alert>
    );
  }

  return (
    <div className={styles.list}>
      {data.map((rootFolder) => (
        <RootFolderRow
          key={rootFolder.id}
          {...rootFolder}
          isSelectable={isSelectable}
        />
      ))}
    </div>
  );
}

export default RootFolders;
