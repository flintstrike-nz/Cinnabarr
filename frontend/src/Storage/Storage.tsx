import React, { useCallback, useEffect, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  StorageDrive,
  StorageLibrary,
  StorageSettings,
  useStoragePlan,
  useStorageSettings,
} from './useStorage';
import styles from './Storage.module.css';

function Drive({ drive }: { drive: StorageDrive }) {
  const total = drive.totalSpace || 1;
  const used = Math.max(0, total - drive.freeSpace);
  const usedPct = Math.min(100, (used / total) * 100);
  const incomingPct = Math.min(
    100 - usedPct,
    (drive.incomingBytes / total) * 100
  );

  return (
    <div className={styles.drive}>
      <div className={styles.driveHeader}>
        <span className={styles.path}>{drive.path}</span>
        <span>
          {formatBytes(drive.freeSpace)} {translate('StorageFree')} /{' '}
          {formatBytes(drive.totalSpace)}
        </span>
      </div>

      <div className={styles.bar}>
        <div className={styles.used} style={{ width: `${usedPct}%` }} />
        <div className={styles.incoming} style={{ width: `${incomingPct}%` }} />
      </div>

      <div className={styles.facts}>
        {translate('StorageIncoming', {
          size: formatBytes(drive.incomingBytes),
        })}
      </div>
    </div>
  );
}

function Library({ library }: { library: StorageLibrary }) {
  return (
    <div className={styles.library}>
      <h3>
        {library.library === 'series'
          ? translate('Series')
          : translate('Movies')}
      </h3>

      {library.drives.length < 2 ? (
        <p>{translate('StorageNeedTwoDrives')}</p>
      ) : (
        library.drives.map((drive) => <Drive key={drive.path} drive={drive} />)
      )}

      {library.moves.length ? (
        <table className={styles.moves}>
          <thead>
            <tr>
              <th>{translate('Title')}</th>
              <th>{translate('Size')}</th>
              <th>{translate('StorageFrom')}</th>
              <th>{translate('StorageTo')}</th>
            </tr>
          </thead>
          <tbody>
            {library.moves.map((move) => (
              <tr key={`${move.mediaType}-${move.id}`}>
                <td>{move.title}</td>
                <td>{formatBytes(move.size)}</td>
                <td>{move.fromDrive}</td>
                <td>{move.toDrive}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}

const NUMBER_FIELDS: {
  key: keyof StorageSettings;
  label: string;
  min: number;
}[] = [
  { key: 'intervalHours', label: 'StorageIntervalHours', min: 1 },
  { key: 'tolerancePercent', label: 'StorageTolerancePercent', min: 1 },
  { key: 'reservePercent', label: 'StorageReservePercent', min: 0 },
  { key: 'maxMoveGb', label: 'StorageMaxMoveGb', min: 1 },
  { key: 'forecastDays', label: 'StorageForecastDays', min: 0 },
];

// Drive management: how full each drive of a library is, what the balancer would move, and its settings.
function Storage() {
  const { data, isLoading, error } = useStoragePlan();
  const { settings, save, isSaving, saveError } = useStorageSettings();
  const executeCommand = useExecuteCommand();
  const isBalancing = useCommandExecuting('StorageBalance');
  const [draft, setDraft] = useState<StorageSettings | null>(null);
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);

  useEffect(() => {
    if (settings) {
      setDraft(settings);
    }
  }, [settings]);

  const handleEnabledChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setDraft((d) => d && { ...d, enabled: event.target.checked });
    },
    []
  );

  const handleNumberChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const key = event.target.name as keyof StorageSettings;

      setDraft((d) => d && { ...d, [key]: Number(event.target.value) });
    },
    []
  );

  const handleSavePress = useCallback(() => {
    if (draft) {
      save(draft);
    }
  }, [draft, save]);

  const handleBalancePress = useCallback(() => setIsConfirmOpen(true), []);
  const handleConfirmClose = useCallback(() => setIsConfirmOpen(false), []);
  const handleConfirm = useCallback(() => {
    setIsConfirmOpen(false);
    executeCommand({ name: CommandNames.StorageBalance });
  }, [executeCommand]);

  const plannedMoves = data.reduce((n, l) => n + l.moves.length, 0);

  return (
    <PageContent title={translate('Storage')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Settings')}
          title={translate('Storage')}
        />

        <p>{translate('StorageCaption')}</p>

        {isLoading ? <LoadingIndicator /> : null}
        {error ? <div>{getErrorMessage(error)}</div> : null}

        {data.map((library) => (
          <Library key={library.library} library={library} />
        ))}

        <Button
          isDisabled={isBalancing || !plannedMoves}
          onPress={handleBalancePress}
        >
          {translate('StorageBalanceNow', { count: plannedMoves })}
        </Button>

        {draft ? (
          <div className={styles.form}>
            <h3>{translate('Schedule')}</h3>

            <label>
              <input
                type="checkbox"
                checked={draft.enabled}
                onChange={handleEnabledChange}
              />{' '}
              {translate('StorageEnabled')}
            </label>

            {NUMBER_FIELDS.map((field) => (
              <label key={field.key}>
                {translate(field.label)}
                <input
                  type="number"
                  name={field.key}
                  min={field.min}
                  value={draft[field.key] as number}
                  onChange={handleNumberChange}
                />
              </label>
            ))}

            <Button
              kind={kinds.PRIMARY}
              isDisabled={isSaving}
              onPress={handleSavePress}
            >
              {translate('Save')}
            </Button>

            {saveError ? <div>{getErrorMessage(saveError)}</div> : null}
          </div>
        ) : null}

        <ConfirmModal
          isOpen={isConfirmOpen}
          title={translate('StorageBalanceNowTitle')}
          message={translate('StorageBalanceConfirm', { count: plannedMoves })}
          confirmLabel={translate('StorageMove')}
          onConfirm={handleConfirm}
          onCancel={handleConfirmClose}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default Storage;
