import React, { useCallback, useState } from 'react';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { icons, kinds } from 'Helpers/Props';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { useSortedTagList } from 'Tags/useTags';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import MoviePoster from './MoviePoster';
import useBulkMovies from './useBulkMovies';
import { useMovies } from './useMovies';
import styles from './Movie.module.css';

function MoviesIndex() {
  const { data: movies, isLoading, error } = useMovies();
  const profiles = useQualityProfilesData();
  const tags = useSortedTagList();
  const { applyToMovies, isApplying, applyError } = useBulkMovies();
  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);

  const selected = movies.filter((movie) => selectedIds.includes(movie.id));
  const hasSelection = selected.length > 0;

  const handleToggle = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const id = Number(event.target.dataset.id);

      setSelectedIds((ids) =>
        event.target.checked ? [...ids, id] : ids.filter((i) => i !== id)
      );
    },
    []
  );

  const handleToggleAll = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setSelectedIds(event.target.checked ? movies.map((m) => m.id) : []);
    },
    [movies]
  );

  const handleSearchPress = useCallback(() => {
    applyToMovies({ movies: selected, action: { type: 'search' } });
  }, [applyToMovies, selected]);

  const handleMonitorPress = useCallback(() => {
    applyToMovies({
      movies: selected,
      action: { type: 'monitor', monitored: true },
    });
  }, [applyToMovies, selected]);

  const handleUnmonitorPress = useCallback(() => {
    applyToMovies({
      movies: selected,
      action: { type: 'monitor', monitored: false },
    });
  }, [applyToMovies, selected]);

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const qualityProfileId = Number(event.target.value);

      if (qualityProfileId) {
        applyToMovies({
          movies: selected,
          action: { type: 'profile', qualityProfileId },
        });
      }
    },
    [applyToMovies, selected]
  );

  const handleAddTagChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const tagId = Number(event.target.value);

      if (tagId) {
        applyToMovies({ movies: selected, action: { type: 'addTag', tagId } });
      }
    },
    [applyToMovies, selected]
  );

  const handleRemoveTagChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const tagId = Number(event.target.value);

      if (tagId) {
        applyToMovies({
          movies: selected,
          action: { type: 'removeTag', tagId },
        });
      }
    },
    [applyToMovies, selected]
  );

  const handleDeletePress = useCallback(() => {
    setIsDeleteOpen(true);
  }, []);

  const handleDeleteCancel = useCallback(() => {
    setIsDeleteOpen(false);
  }, []);

  const handleDeleteConfirm = useCallback(() => {
    setIsDeleteOpen(false);
    setSelectedIds([]);
    applyToMovies({
      movies: selected,
      action: { type: 'delete', deleteFiles: false },
    });
  }, [applyToMovies, selected]);

  return (
    <PageContent title={translate('Movies')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Media')}
          title={translate('Movies')}
          actions={
            <Button to="/add/movie" kind={kinds.PRIMARY}>
              {translate('AddMovie')}
            </Button>
          }
        />

        {isLoading ? <LoadingIndicator /> : null}

        {error ? <div>{getErrorMessage(error)}</div> : null}

        {!isLoading && !error && !movies.length ? (
          <div>
            <p>{translate('NoMoviesHaveBeenAdded')}</p>
            <Button to="/add/movie" kind={kinds.PRIMARY}>
              {translate('AddMovie')}
            </Button>
          </div>
        ) : null}

        {hasSelection ? (
          <div className={styles.actions}>
            <span>{selected.length}</span>

            <Button isDisabled={isApplying} onPress={handleSearchPress}>
              {translate('SearchMovie')}
            </Button>

            <Button isDisabled={isApplying} onPress={handleMonitorPress}>
              {translate('Monitored')}
            </Button>

            <Button isDisabled={isApplying} onPress={handleUnmonitorPress}>
              {translate('Unmonitored')}
            </Button>

            <select
              className={styles.select}
              aria-label={translate('QualityProfile')}
              value=""
              disabled={isApplying}
              onChange={handleProfileChange}
            >
              <option value="">{translate('QualityProfile')}</option>
              {profiles.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>

            {tags.length ? (
              <>
                <select
                  className={styles.select}
                  aria-label={translate('AddTag')}
                  value=""
                  disabled={isApplying}
                  onChange={handleAddTagChange}
                >
                  <option value="">{translate('AddTag')}</option>
                  {tags.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.label}
                    </option>
                  ))}
                </select>

                <select
                  className={styles.select}
                  aria-label={translate('RemoveTag')}
                  value=""
                  disabled={isApplying}
                  onChange={handleRemoveTagChange}
                >
                  <option value="">{translate('RemoveTag')}</option>
                  {tags.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.label}
                    </option>
                  ))}
                </select>
              </>
            ) : null}

            <Button
              kind={kinds.DANGER}
              isDisabled={isApplying}
              onPress={handleDeletePress}
            >
              {translate('DeleteMovie')}
            </Button>

            {applyError ? <span>{getErrorMessage(applyError)}</span> : null}
          </div>
        ) : null}

        <ConfirmModal
          isOpen={isDeleteOpen}
          kind={kinds.DANGER}
          title={translate('DeleteMovie')}
          message={translate('DeleteMovieHelpText')}
          confirmLabel={translate('Delete')}
          onConfirm={handleDeleteConfirm}
          onCancel={handleDeleteCancel}
        />

        {movies.length ? (
          <table className={styles.table}>
            <thead>
              <tr>
                <th>
                  <input
                    type="checkbox"
                    aria-label={translate('SelectAll')}
                    checked={selected.length === movies.length}
                    onChange={handleToggleAll}
                  />
                </th>
                <th />
                <th>{translate('Title')}</th>
                <th>{translate('Year')}</th>
                <th>{translate('Status')}</th>
                <th>{translate('QualityProfile')}</th>
                <th>{translate('Size')}</th>
              </tr>
            </thead>
            <tbody>
              {[...movies]
                .sort((a, b) => a.title.localeCompare(b.title))
                .map((movie) => (
                  <tr key={movie.id}>
                    <td>
                      <input
                        type="checkbox"
                        aria-label={translate('SelectRow')}
                        data-id={movie.id}
                        checked={selectedIds.includes(movie.id)}
                        onChange={handleToggle}
                      />
                    </td>
                    <td>
                      <MoviePoster movie={movie} small={true} />
                    </td>
                    <td>
                      <Icon
                        name={
                          movie.monitored ? icons.MONITORED : icons.UNMONITORED
                        }
                        title={
                          movie.monitored
                            ? translate('Monitored')
                            : translate('Unmonitored')
                        }
                      />{' '}
                      <Link to={`/movie/${movie.id}`}>{movie.title}</Link>
                    </td>
                    <td>{movie.year}</td>
                    <td>
                      {movie.hasFile
                        ? translate('Downloaded')
                        : translate('Missing')}
                    </td>
                    <td>
                      {profiles.find((p) => p.id === movie.qualityProfileId)
                        ?.name ?? ''}
                    </td>
                    <td>
                      {movie.hasFile ? formatBytes(movie.sizeOnDisk) : ''}
                    </td>
                  </tr>
                ))}
            </tbody>
          </table>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default MoviesIndex;
