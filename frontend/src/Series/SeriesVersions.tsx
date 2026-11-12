import React, { useCallback, useMemo, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import styles from 'Movie/Movie.module.css';
import SeasonInteractiveSearchModal from 'Series/Search/SeasonInteractiveSearchModal';
import { useSingleSeries } from 'Series/useSeries';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  SeriesVersion,
  useAddSeriesVersion,
  useDeleteSeriesVersion,
  useSearchSeriesVersion,
  useSeriesVersions,
  useUpdateSeriesVersion,
} from './useSeriesVersions';

interface VersionRowProps {
  version: SeriesVersion;
  seasonNumbers: number[];
  episodeCounts: Record<number, number>;
}

function VersionRow({
  version,
  seasonNumbers,
  episodeCounts,
}: VersionRowProps) {
  const versionId = version.id ?? 0;
  const profiles = useQualityProfilesData();
  const { updateVersion, isUpdating, updateError } =
    useUpdateSeriesVersion(versionId);
  const { deleteVersion, isDeleting, deleteError } =
    useDeleteSeriesVersion(versionId);
  const { searchVersion, isSearching, searchError } = useSearchSeriesVersion();
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const [isSearchOpen, setIsSearchOpen] = useState(false);
  const [searchSeason, setSearchSeason] = useState<number | null>(null);
  const selectedSeason =
    searchSeason ?? seasonNumbers.find((n) => n > 0) ?? seasonNumbers[0] ?? 0;

  const handleSearchPress = useCallback(() => {
    searchVersion(version.seriesId, versionId);
  }, [searchVersion, version.seriesId, versionId]);

  const handleSeasonChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setSearchSeason(Number(event.target.value));
    },
    []
  );

  const handleInteractiveSearchPress = useCallback(() => {
    setIsSearchOpen(true);
  }, []);

  const handleInteractiveSearchClose = useCallback(() => {
    setIsSearchOpen(false);
  }, []);

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
          <>
            <Button isDisabled={isSearching} onPress={handleSearchPress}>
              {translate('Search')}
            </Button>

            <select
              className={styles.select}
              aria-label={translate('Season')}
              value={selectedSeason}
              onChange={handleSeasonChange}
            >
              {seasonNumbers.map((seasonNumber) => (
                <option key={seasonNumber} value={seasonNumber}>
                  {seasonNumber === 0
                    ? translate('Specials')
                    : `${translate('Season')} ${seasonNumber}`}
                </option>
              ))}
            </select>

            <Button onPress={handleInteractiveSearchPress}>
              {translate('InteractiveSearch')}
            </Button>
          </>
        )}

        {version.isDefault ? null : (
          <Button
            kind={kinds.DANGER}
            isDisabled={isDeleting || version.episodeFileCount > 0}
            onPress={handleDeletePress}
          >
            {translate('Delete')}
          </Button>
        )}

        {searchError ? <p>{getErrorMessage(searchError)}</p> : null}
        {updateError ? <p>{getErrorMessage(updateError)}</p> : null}
        {deleteError ? <p>{getErrorMessage(deleteError)}</p> : null}

        <SeasonInteractiveSearchModal
          isOpen={isSearchOpen}
          episodeCount={episodeCounts[selectedSeason] ?? 0}
          seriesId={version.seriesId}
          seasonNumber={selectedSeason}
          versionSlotId={versionId}
          onModalClose={handleInteractiveSearchClose}
        />

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
  const series = useSingleSeries(seriesId);
  const seasonNumbers = useMemo(
    () =>
      (series?.seasons ?? [])
        .map((season) => season.seasonNumber)
        .sort((a, b) => a - b),
    [series]
  );
  const episodeCounts = useMemo(
    () =>
      Object.fromEntries(
        (series?.seasons ?? []).map((season) => [
          season.seasonNumber,
          season.statistics?.totalEpisodeCount ?? 0,
        ])
      ),
    [series]
  );
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
            <VersionRow
              key={version.id ?? 0}
              version={version}
              seasonNumbers={seasonNumbers}
              episodeCounts={episodeCounts}
            />
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
