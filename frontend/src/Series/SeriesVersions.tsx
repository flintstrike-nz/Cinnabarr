import React, { useCallback, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import styles from 'Movie/Movie.module.css';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  SeriesVersion,
  useAddSeriesVersion,
  useDeleteSeriesVersion,
  useSeriesVersions,
  useUpdateSeriesVersion,
} from './useSeriesVersions';

interface VersionRowProps {
  version: SeriesVersion;
}

function VersionRow({ version }: VersionRowProps) {
  const versionId = version.id ?? 0;
  const profiles = useQualityProfilesData();
  const { updateVersion, isUpdating, updateError } =
    useUpdateSeriesVersion(versionId);
  const { deleteVersion, isDeleting, deleteError } =
    useDeleteSeriesVersion(versionId);
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      updateVersion({
        ...version,
        qualityProfileId: Number(event.target.value),
      });
    },
    [version, updateVersion]
  );

  const handleMonitoredChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      updateVersion({ ...version, monitored: event.target.checked });
    },
    [version, updateVersion]
  );

  const handleDeletePress = useCallback(() => {
    setIsConfirmOpen(true);
  }, []);

  const handleDeleteCancel = useCallback(() => {
    setIsConfirmOpen(false);
  }, []);

  const handleDeleteConfirm = useCallback(() => {
    setIsConfirmOpen(false);
    deleteVersion();
  }, [deleteVersion]);

  const profile = profiles.find((p) => p.id === version.qualityProfileId);

  return (
    <tr>
      <td>{version.isDefault ? translate('DefaultVersion') : version.label}</td>
      <td>
        {version.isDefault ? (
          profile?.name
        ) : (
          <select
            className={styles.select}
            aria-label={translate('QualityProfile')}
            value={version.qualityProfileId}
            disabled={isUpdating}
            onChange={handleProfileChange}
          >
            {profiles.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        )}
      </td>
      <td>
        {version.isDefault ? (
          translate(version.monitored ? 'Monitored' : 'Unmonitored')
        ) : (
          <input
            type="checkbox"
            aria-label={translate('Monitored')}
            checked={version.monitored}
            disabled={isUpdating}
            onChange={handleMonitoredChange}
          />
        )}
      </td>
      <td>{version.episodeFileCount}</td>
      <td className={styles.actions}>
        {version.isDefault ? null : (
          <Button
            kind={kinds.DANGER}
            isDisabled={isDeleting || version.episodeFileCount > 0}
            onPress={handleDeletePress}
          >
            {translate('Delete')}
          </Button>
        )}

        {updateError ? <p>{getErrorMessage(updateError)}</p> : null}
        {deleteError ? <p>{getErrorMessage(deleteError)}</p> : null}

        <ConfirmModal
          isOpen={isConfirmOpen}
          kind={kinds.DANGER}
          title={translate('DeleteVersion')}
          message={translate('DeleteVersionConfirmation', {
            label: version.label,
          })}
          confirmLabel={translate('Delete')}
          onConfirm={handleDeleteConfirm}
          onCancel={handleDeleteCancel}
        />
      </td>
    </tr>
  );
}

interface SeriesVersionsProps {
  seriesId: number;
}

// The versions of a series (for example 4K and HD): each has its own quality profile and its own file for each episode.
function SeriesVersions({ seriesId }: SeriesVersionsProps) {
  const profiles = useQualityProfilesData();
  const { data: versions } = useSeriesVersions(seriesId);
  const { addVersion, isAdding, addError } = useAddSeriesVersion();
  const [label, setLabel] = useState('');
  const [profileId, setProfileId] = useState(0);

  const handleLabelChange = useCallback(({ value }: InputChanged<string>) => {
    setLabel(value);
  }, []);

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setProfileId(Number(event.target.value));
    },
    []
  );

  const selectedProfileId = profileId || profiles[0]?.id || 0;

  const handleAddPress = useCallback(() => {
    addVersion(
      {
        seriesId,
        label,
        qualityProfileId: selectedProfileId,
        monitored: true,
      },
      { onSuccess: () => setLabel('') }
    );
  }, [addVersion, seriesId, label, selectedProfileId]);

  return (
    <section>
      <h2>{translate('Versions')}</h2>

      <table className={styles.table}>
        <thead>
          <tr>
            <th>{translate('Version')}</th>
            <th>{translate('QualityProfile')}</th>
            <th>{translate('Monitored')}</th>
            <th>{translate('SeriesVersionFiles')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {versions.map((version) => (
            <VersionRow key={version.id ?? 0} version={version} />
          ))}
        </tbody>
      </table>

      <div className={styles.actions}>
        <TextInput
          name="newSeriesVersionLabel"
          value={label}
          placeholder={translate('VersionLabelPlaceholder')}
          onChange={handleLabelChange}
        />

        <select
          className={styles.select}
          aria-label={translate('QualityProfile')}
          value={selectedProfileId}
          onChange={handleProfileChange}
        >
          {profiles.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>

        <Button
          kind={kinds.SUCCESS}
          isDisabled={isAdding || !label.trim()}
          onPress={handleAddPress}
        >
          {translate('AddVersion')}
        </Button>
      </div>

      {addError ? <p>{getErrorMessage(addError)}</p> : null}
    </section>
  );
}

export default SeriesVersions;
