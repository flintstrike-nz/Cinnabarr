import React, { useCallback, useMemo, useState } from 'react';
import { useExecuteCommand } from 'Commands/useCommands';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import MenuContent from 'Components/Menu/MenuContent';
import SortMenu from 'Components/Menu/SortMenu';
import SortMenuItem from 'Components/Menu/SortMenuItem';
import ViewMenu from 'Components/Menu/ViewMenu';
import ViewMenuItem from 'Components/Menu/ViewMenuItem';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageJumpBar, { PageJumpBarItems } from 'Components/Page/PageJumpBar';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import PageToolbarSpacer from 'Components/Page/Toolbar/PageToolbarSpacer';
import DoctorBadge from 'Doctor/DoctorBadge';
import { align, icons, kinds } from 'Helpers/Props';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { useSortedTagList } from 'Tags/useTags';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  filterMovies,
  MovieFilterKey,
  sortMovies,
  useMovieIndexOptions,
} from './Index/movieIndexOptions';
import MovieIndexPoster from './Index/MovieIndexPoster';
import MoviePoster from './MoviePoster';
import useBulkMovies from './useBulkMovies';
import { useMovies } from './useMovies';
import posterStyles from './Index/MovieIndexPoster.module.css';
import styles from './Movie.module.css';

function MoviesIndex() {
  const { data: allMovies, isLoading, error } = useMovies();
  const executeCommand = useExecuteCommand();
  const {
    view,
    sortKey,
    sortDirection,
    filterKey,
    setView,
    setFilter,
    setSort,
  } = useMovieIndexOptions();
  const [isSelectMode, setIsSelectMode] = useState(false);
  const movies = useMemo(
    () =>
      sortMovies(
        filterMovies(allMovies, filterKey as MovieFilterKey),
        sortKey,
        sortDirection
      ),
    [allMovies, filterKey, sortKey, sortDirection]
  );
  const jumpBarItems = useMemo<PageJumpBarItems>(() => {
    const characters: Record<string, number> = {};
    const order: string[] = [];

    if (sortKey === 'sortTitle') {
      movies.forEach((movie, index) => {
        const first = (movie.sortTitle ?? movie.title).charAt(0).toUpperCase();
        const key = /[A-Z]/.test(first) ? first : '#';

        if (!(key in characters)) {
          characters[key] = index;
          order.push(key);
        }
      });
    }

    return { characters, order };
  }, [movies, sortKey]);

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

  const handleSelectModePress = useCallback(() => {
    setIsSelectMode((value) => !value);
    setSelectedIds([]);
  }, []);

  const handleUpdateAllPress = useCallback(() => {
    executeCommand({ name: 'RefreshMovie' });
  }, [executeCommand]);

  const handleSearchMissingPress = useCallback(() => {
    executeCommand({ name: 'MissingMoviesSearch' });
  }, [executeCommand]);

  const handleJumpPress = useCallback(
    (character: string) => {
      const index = jumpBarItems.characters[character];
      const id = movies[index]?.id;

      document
        .querySelector(`[data-movie-id="${id}"]`)
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    },
    [jumpBarItems, movies]
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
      <PageToolbar>
        <PageToolbarButton
          label={translate('UpdateAll')}
          iconName={icons.REFRESH}
          isDisabled={!allMovies.length}
          onPress={handleUpdateAllPress}
        />
        <PageToolbarButton
          label={translate('SearchMissing')}
          iconName={icons.SEARCH}
          isDisabled={!allMovies.length}
          onPress={handleSearchMissingPress}
        />
        <DoctorBadge>
          <PageToolbarButton
            label={translate('Doctor')}
            iconName={icons.DOCTOR}
            to="/doctor"
          />
        </DoctorBadge>

        <PageToolbarSeparator />

        <PageToolbarButton
          label={
            isSelectMode ? translate('StopSelecting') : translate('Select')
          }
          iconName={isSelectMode ? icons.SERIES_ENDED : icons.CHECK}
          onPress={handleSelectModePress}
        />

        <PageToolbarSpacer />

        <PageToolbarButton
          label={translate('AddMovie')}
          iconName={icons.ADD}
          to="/add/movie"
        />

        <ViewMenu alignMenu={align.RIGHT}>
          <MenuContent>
            <ViewMenuItem name="table" selectedView={view} onPress={setView}>
              {translate('Table')}
            </ViewMenuItem>
            <ViewMenuItem name="posters" selectedView={view} onPress={setView}>
              {translate('Posters')}
            </ViewMenuItem>
          </MenuContent>
        </ViewMenu>

        <SortMenu alignMenu={align.RIGHT}>
          <MenuContent>
            {(
              [
                ['sortTitle', 'Title'],
                ['year', 'Year'],
                ['added', 'Added'],
                ['sizeOnDisk', 'SizeOnDisk'],
              ] as const
            ).map(([name, label]) => (
              <SortMenuItem
                key={name}
                name={name}
                sortKey={sortKey}
                sortDirection={sortDirection}
                onPress={setSort}
              >
                {translate(label)}
              </SortMenuItem>
            ))}
          </MenuContent>
        </SortMenu>

        <FilterMenu
          alignMenu="right"
          selectedFilterKey={filterKey}
          customFilters={[]}
          filters={[
            { key: 'all', label: () => translate('All'), filters: [] },
            {
              key: 'monitored',
              label: () => translate('Monitored'),
              filters: [],
            },
            {
              key: 'unmonitored',
              label: () => translate('Unmonitored'),
              filters: [],
            },
            { key: 'missing', label: () => translate('Missing'), filters: [] },
            {
              key: 'downloaded',
              label: () => translate('Downloaded'),
              filters: [],
            },
          ]}
          onFilterSelect={setFilter}
        />
      </PageToolbar>

      <PageContentBody>
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

        {movies.length && view === 'posters' ? (
          <div className={posterStyles.grid}>
            {movies.map((movie) => (
              <div key={movie.id} data-movie-id={movie.id}>
                <MovieIndexPoster
                  movie={movie}
                  profileName={
                    profiles.find((p) => p.id === movie.qualityProfileId)?.name
                  }
                  isSelectMode={isSelectMode}
                  isSelected={selectedIds.includes(movie.id)}
                  onToggle={handleToggle}
                />
              </div>
            ))}
          </div>
        ) : null}

        {movies.length && view === 'table' ? (
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
      {view === 'posters' && jumpBarItems.order.length ? (
        <PageJumpBar items={jumpBarItems} onItemPress={handleJumpPress} />
      ) : null}
    </PageContent>
  );
}

export default MoviesIndex;
