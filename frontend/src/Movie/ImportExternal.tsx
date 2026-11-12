import React, { useCallback, useMemo, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import styles from './Movie.module.css';

interface ExternalImportItem {
  name: string;
  status: 'add' | 'exists' | 'skipped';
  reason?: string;
}

interface ExternalImportRequest {
  kind: string;
  folder: string;
  pathFrom: string;
  pathTo: string;
}

const NO_ITEMS: ExternalImportItem[] = [];

const STATUS_LABELS: Record<ExternalImportItem['status'], string> = {
  add: 'ImportExternalStatusAdd',
  exists: 'ImportExternalStatusExists',
  skipped: 'ImportExternalStatusSkipped',
};

// Bring the library (or the Torznab indexers) of another Sonarr, Radarr or Prowlarr install over: the data folder is
// read, never changed, and what would be added is listed first.
function ImportExternal() {
  const [kind, setKind] = useState('sonarr');
  const [folder, setFolder] = useState('');
  const [pathFrom, setPathFrom] = useState('');
  const [pathTo, setPathTo] = useState('');
  const [started, setStarted] = useState(false);

  const {
    mutate: preview,
    data: items = NO_ITEMS,
    isPending: isPreviewing,
    error: previewError,
    isSuccess: hasPreview,
  } = useApiMutation<ExternalImportItem[], ExternalImportRequest>({
    path: '/import/external/preview',
    method: 'POST',
  });

  const {
    mutate: runImport,
    isPending: isStarting,
    error: importError,
  } = useApiMutation<unknown, Record<string, string>>({
    path: '/command',
    method: 'POST',
    mutationOptions: { onSuccess: () => setStarted(true) },
  });

  const request = useMemo(
    () => ({ kind, folder, pathFrom, pathTo }),
    [kind, folder, pathFrom, pathTo]
  );

  const handleKindChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setKind(event.target.value);
      setStarted(false);
    },
    []
  );

  const handleFolderChange = useCallback(({ value }: InputChanged<string>) => {
    setFolder(value);
    setStarted(false);
  }, []);

  const handlePathFromChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setPathFrom(value);
    },
    []
  );

  const handlePathToChange = useCallback(({ value }: InputChanged<string>) => {
    setPathTo(value);
  }, []);

  const handlePreviewPress = useCallback(() => {
    setStarted(false);
    preview(request);
  }, [preview, request]);

  const handleImportPress = useCallback(() => {
    runImport({ name: 'ExternalInstallImport', ...request });
  }, [runImport, request]);

  const toAdd = items.filter((item) => item.status === 'add').length;

  return (
    <PageContent title={translate('ImportExternal')}>
      <PageContentBody>
        <PageHeading title={translate('ImportExternal')} />

        <p>{translate('ImportExternalCaption')}</p>

        <div className={styles.actions}>
          <select
            className={styles.select}
            aria-label={translate('ImportExternalKind')}
            value={kind}
            onChange={handleKindChange}
          >
            <option value="sonarr">Sonarr</option>
            <option value="radarr">Radarr</option>
            <option value="prowlarr">Prowlarr</option>
            <option value="qbittorrent">qBittorrent</option>
          </select>

          <TextInput
            name="externalFolder"
            value={folder}
            placeholder={translate('ImportExternalFolder')}
            onChange={handleFolderChange}
          />
        </div>

        <div className={styles.actions}>
          <TextInput
            name="externalPathFrom"
            value={pathFrom}
            placeholder={translate('ImportExternalPathFrom')}
            onChange={handlePathFromChange}
          />

          <TextInput
            name="externalPathTo"
            value={pathTo}
            placeholder={translate('ImportExternalPathTo')}
            onChange={handlePathToChange}
          />

          <Button
            kind={kinds.PRIMARY}
            isDisabled={!folder || isPreviewing}
            onPress={handlePreviewPress}
          >
            {translate('ImportExternalPreview')}
          </Button>
        </div>

        {previewError ? <p>{getErrorMessage(previewError)}</p> : null}

        {hasPreview ? (
          <>
            <p>{translate('ImportExternalWillAdd', { count: toAdd })}</p>

            <Button
              kind={kinds.SUCCESS}
              isDisabled={!toAdd || isStarting || started}
              onPress={handleImportPress}
            >
              {translate('ImportExternalStart')}
            </Button>

            {started ? <p>{translate('ImportExternalStarted')}</p> : null}
            {importError ? <p>{getErrorMessage(importError)}</p> : null}

            <table className={styles.table}>
              <thead>
                <tr>
                  <th>{translate('Name')}</th>
                  <th>{translate('Status')}</th>
                  <th>{translate('Message')}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.name}>
                    <td>{item.name}</td>
                    <td>{translate(STATUS_LABELS[item.status])}</td>
                    <td>{item.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default ImportExternal;
