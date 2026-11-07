import React, { useCallback, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import Movie, { MovieStatus } from './Movie';
import { useSaveMovie } from './useMovies';
import styles from './Movie.module.css';

const AVAILABILITY_OPTIONS: { value: MovieStatus; label: () => string }[] = [
  { value: 'announced', label: () => translate('MovieStatusAnnounced') },
  { value: 'inCinemas', label: () => translate('MovieStatusInCinemas') },
  { value: 'released', label: () => translate('MovieStatusReleased') },
];

interface MovieEditPanelProps {
  movie: Movie;
  onDone: () => void;
}

// Changes what the movie is searched for with: its quality profile and when it counts as available.
function MovieEditPanel({ movie, onDone }: MovieEditPanelProps) {
  const profiles = useQualityProfilesData();
  const { saveMovie, isSaving, saveError } = useSaveMovie();
  const [path, setPath] = useState(movie.path);
  const [moveFiles, setMoveFiles] = useState(true);
  const [qualityProfileId, setQualityProfileId] = useState(
    movie.qualityProfileId
  );
  const [minimumAvailability, setMinimumAvailability] = useState(
    movie.minimumAvailability
  );

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setQualityProfileId(Number(event.target.value));
    },
    []
  );

  const handleAvailabilityChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setMinimumAvailability(event.target.value as MovieStatus);
    },
    []
  );

  const handlePathChange = useCallback(({ value }: InputChanged<string>) => {
    setPath(value);
  }, []);

  const handleMoveFilesChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMoveFiles(event.target.checked);
    },
    []
  );

  const handleSavePress = useCallback(() => {
    saveMovie(
      {
        movie: { ...movie, qualityProfileId, minimumAvailability, path },
        moveFiles: moveFiles && path !== movie.path,
      },
      { onSuccess: onDone }
    );
  }, [
    movie,
    qualityProfileId,
    minimumAvailability,
    path,
    moveFiles,
    saveMovie,
    onDone,
  ]);

  return (
    <div className={styles.actions}>
      <select
        className={styles.select}
        aria-label={translate('QualityProfile')}
        value={qualityProfileId}
        onChange={handleProfileChange}
      >
        {profiles.map((p) => (
          <option key={p.id} value={p.id}>
            {p.name}
          </option>
        ))}
      </select>

      <select
        className={styles.select}
        aria-label={translate('MinimumAvailability')}
        value={minimumAvailability}
        onChange={handleAvailabilityChange}
      >
        {AVAILABILITY_OPTIONS.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label()}
          </option>
        ))}
      </select>

      <TextInput name="moviePath" value={path} onChange={handlePathChange} />

      {path === movie.path ? null : (
        <label>
          <input
            type="checkbox"
            checked={moveFiles}
            onChange={handleMoveFilesChange}
          />{' '}
          {translate('MoveFiles')}
        </label>
      )}

      <Button
        kind={kinds.SUCCESS}
        isDisabled={isSaving}
        onPress={handleSavePress}
      >
        {translate('Save')}
      </Button>

      <Button onPress={onDone}>{translate('Cancel')}</Button>

      {saveError ? <p>{getErrorMessage(saveError)}</p> : null}
    </div>
  );
}

export default MovieEditPanel;
