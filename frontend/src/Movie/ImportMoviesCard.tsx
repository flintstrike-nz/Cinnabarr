import classNames from 'classnames';
import React, { useCallback, useEffect, useState } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { kinds } from 'Helpers/Props';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  ImportMovieEligibility,
  ImportMovieStatus,
  setImportMovieMatch,
  setImportMoviesSelected,
  UnmappedMovieFolder,
} from './importMoviesStore';
import Movie from './Movie';
import MoviePoster from './MoviePoster';
import { useLookupMovies } from './useMovies';
import styles from './ImportMovies.module.css';

// The lookup result that fits the folder: the one from the folder's year when there is one.
function pickMatch(movies: readonly Movie[], year: number) {
  return movies.find((movie) => year > 0 && movie.year === year) ?? movies[0];
}

const ELIGIBILITY_LABELS: Partial<Record<ImportMovieEligibility, string>> = {
  unmatched: 'NoResultsFound',
  existing: 'AlreadyInLibrary',
  duplicate: 'DuplicateMatch',
};

interface ImportMoviesCardProps {
  folder: UnmappedMovieFolder;
  eligibility: ImportMovieEligibility;
  isSelected: boolean;
  status?: ImportMovieStatus;
  libraryMovie?: Movie;
  onMergePress: (folder: UnmappedMovieFolder, movie: Movie) => void;
}

// One folder or loose file of the root folder: what it was matched to, and whether it can be imported.
function ImportMoviesCard({
  folder,
  eligibility,
  isSelected,
  status,
  libraryMovie,
  onMergePress,
}: ImportMoviesCardProps) {
  const term = folder.parsedYear
    ? `${folder.parsedTitle} ${folder.parsedYear}`
    : folder.parsedTitle;
  const { data, isFetching } = useLookupMovies(term);
  const [chosenTmdbId, setChosenTmdbId] = useState(0);

  const match = isFetching
    ? undefined
    : data.find((movie) => movie.tmdbId === chosenTmdbId) ??
      pickMatch(data, folder.parsedYear) ??
      null;

  useEffect(() => {
    setImportMovieMatch(folder.path, match);
  }, [folder.path, match]);

  const handleMatchChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setChosenTmdbId(Number(event.target.value));
    },
    []
  );

  const handleSelectedChange = useCallback(
    ({ value }: CheckInputChanged) => {
      setImportMoviesSelected([folder.path], value);
    },
    [folder.path]
  );

  const handleMergePress = useCallback(() => {
    if (libraryMovie) {
      onMergePress(folder, libraryMovie);
    }
  }, [folder, libraryMovie, onMergePress]);

  const isDone = status?.state === 'done';
  const isWorking = status?.state === 'working';
  const canSelect =
    (eligibility === 'ready' || eligibility === 'duplicate') && !isDone;
  const label = ELIGIBILITY_LABELS[eligibility];

  return (
    <div
      className={classNames(
        styles.row,
        !canSelect && !isDone && styles.needsAttention,
        isDone && styles.done
      )}
    >
      <CheckInput
        containerClassName={styles.check}
        name={folder.path}
        ariaLabel={translate('SelectRow')}
        value={isSelected && canSelect}
        isDisabled={!canSelect || isWorking}
        onChange={handleSelectedChange}
      />

      <div className={styles.poster}>
        {match ? (
          <MoviePoster movie={match} small={true} />
        ) : (
          <div className={styles.posterPlaceholder}>?</div>
        )}
      </div>

      <div className={styles.info}>
        <div className={styles.name}>
          {folder.isFile ? (
            <span className={styles.fileLabel}>
              <Label kind={kinds.INFO}>{translate('LooseFile')}</Label>
            </span>
          ) : null}
          {folder.name}
        </div>

        <div className={styles.match}>
          {match === undefined ? <LoadingIndicator size={16} /> : null}

          {match && data.length > 1 && !isDone ? (
            <select
              className={styles.select}
              aria-label={translate('Movie')}
              value={match.tmdbId}
              onChange={handleMatchChange}
            >
              {data.map((movie) => (
                <option key={movie.tmdbId} value={movie.tmdbId}>
                  {movie.title} ({movie.year})
                </option>
              ))}
            </select>
          ) : null}

          {match && (data.length <= 1 || isDone) ? (
            <span>
              {match.title} ({match.year})
            </span>
          ) : null}
        </div>

        {status?.message ? (
          <div className={styles.message}>{status.message}</div>
        ) : null}
      </div>

      <div className={styles.state}>
        {isWorking ? <LoadingIndicator size={20} /> : null}

        {isDone ? (
          <Label kind={kinds.SUCCESS}>{translate('Imported')}</Label>
        ) : null}

        {status?.state === 'failed' ? (
          <Label kind={kinds.DANGER}>{translate('Failed')}</Label>
        ) : null}

        {!status && label ? (
          <Label kind={kinds.WARNING}>{translate(label)}</Label>
        ) : null}

        {eligibility === 'existing' && libraryMovie && !isDone ? (
          <Button isDisabled={isWorking} onPress={handleMergePress}>
            {translate('MergeIntoMovie')}
          </Button>
        ) : null}
      </div>
    </div>
  );
}

export default ImportMoviesCard;
