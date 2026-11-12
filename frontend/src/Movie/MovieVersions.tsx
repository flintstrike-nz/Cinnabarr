import React, { useCallback, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import { useSearchMovie } from './useMovies';
import {
  useAddVersionSlot,
  useDeleteVersionSlot,
  useUpdateVersionSlot,
  useVersionSlots,
  VersionSlot,
} from './useVersionSlots';
import styles from './Movie.module.css';

interface MovieFileSummary {
  id: number;
  relativePath: string;
}

const NO_FILES: MovieFileSummary[] = [];

interface SlotRowProps {
  slot: VersionSlot;
  fileName: string | undefined;
  onInteractiveSearch: (slotId: number) => void;
}

function SlotRow({ slot, fileName, onInteractiveSearch }: SlotRowProps) {
  const slotId = slot.id ?? 0;
  const profiles = useQualityProfilesData();
  const { searchMovie, isSearching } = useSearchMovie();
  const { updateSlot, isUpdating, updateError } = useUpdateVersionSlot(slotId);
  const { deleteSlot, isDeleting, deleteError } = useDeleteVersionSlot(slotId);
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);

  const handleSearchPress = useCallback(() => {
    searchMovie(slot.movieId, slotId);
  }, [searchMovie, slot.movieId, slotId]);

  const handleInteractivePress = useCallback(() => {
    onInteractiveSearch(slotId);
  }, [onInteractiveSearch, slotId]);

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      updateSlot({ ...slot, qualityProfileId: Number(event.target.value) });
    },
    [slot, updateSlot]
  );

  const handleMonitoredChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      updateSlot({ ...slot, monitored: event.target.checked });
    },
    [slot, updateSlot]
  );

  const handleDeletePress = useCallback(() => {
    setIsConfirmOpen(true);
  }, []);

  const handleDeleteCancel = useCallback(() => {
    setIsConfirmOpen(false);
  }, []);

  const handleDeleteConfirm = useCallback(() => {
    setIsConfirmOpen(false);
    deleteSlot();
  }, [deleteSlot]);

  const profile = profiles.find((p) => p.id === slot.qualityProfileId);
  const monitoredLabel = slot.monitored
    ? translate('Monitored')
    : translate('Unmonitored');

  return (
    <tr>
      <td>{slot.isDefault ? translate('DefaultVersion') : slot.label}</td>
      <td>
        {slot.isDefault ? (
          profile?.name
        ) : (
          <select
            className={styles.select}
            aria-label={translate('QualityProfile')}
            value={slot.qualityProfileId}
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
        {slot.isDefault ? (
          monitoredLabel
        ) : (
          <input
            type="checkbox"
            aria-label={translate('Monitored')}
            checked={slot.monitored}
            disabled={isUpdating}
            onChange={handleMonitoredChange}
          />
        )}
      </td>
      <td>{fileName ?? translate('VersionNoFile')}</td>
      <td className={styles.actions}>
        <Button isDisabled={isSearching} onPress={handleSearchPress}>
          {translate('Search')}
        </Button>

        <Button onPress={handleInteractivePress}>
          {translate('InteractiveSearch')}
        </Button>

        {slot.isDefault ? null : (
          <Button
            kind={kinds.DANGER}
            isDisabled={isDeleting || slot.movieFileId > 0}
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
            label: slot.label,
          })}
          confirmLabel={translate('Delete')}
          onConfirm={handleDeleteConfirm}
          onCancel={handleDeleteCancel}
        />
      </td>
    </tr>
  );
}

interface MovieVersionsProps {
  movieId: number;
  onInteractiveSearch: (slotId: number) => void;
}

// The versions of a movie (for example 4K and HD): each has its own quality profile, file and search.
function MovieVersions({ movieId, onInteractiveSearch }: MovieVersionsProps) {
  const profiles = useQualityProfilesData();
  const { data: slots } = useVersionSlots(movieId);
  const { data: files } = useApiQuery<MovieFileSummary[]>({
    path: '/moviefile',
    queryParams: { movieId },
    queryOptions: { enabled: movieId > 0 },
  });
  const { addSlot, isAdding, addError } = useAddVersionSlot();
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
    addSlot(
      {
        movieId,
        label,
        qualityProfileId: selectedProfileId,
        monitored: true,
      },
      { onSuccess: () => setLabel('') }
    );
  }, [addSlot, movieId, label, selectedProfileId]);

  const fileList = files ?? NO_FILES;

  return (
    <section>
      <h2>{translate('Versions')}</h2>

      <table className={styles.table}>
        <thead>
          <tr>
            <th>{translate('Version')}</th>
            <th>{translate('QualityProfile')}</th>
            <th>{translate('Monitored')}</th>
            <th>{translate('File')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {slots.map((slot) => (
            <SlotRow
              key={slot.id ?? 0}
              slot={slot}
              fileName={
                slot.movieFileId > 0
                  ? fileList.find((f) => f.id === slot.movieFileId)
                      ?.relativePath
                  : undefined
              }
              onInteractiveSearch={onInteractiveSearch}
            />
          ))}
        </tbody>
      </table>

      <div className={styles.actions}>
        <TextInput
          name="versionLabel"
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

        {addError ? <p>{getErrorMessage(addError)}</p> : null}
      </div>
    </section>
  );
}

export default MovieVersions;
