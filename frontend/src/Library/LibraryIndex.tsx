import React, { useCallback, useMemo, useState } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { kinds } from 'Helpers/Props';
import { useMovies } from 'Movie/useMovies';
import SeriesPoster from 'Series/SeriesPoster';
import useSeries from 'Series/useSeries';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import translate from 'Utilities/String/translate';
import useBulkLibrary from './useBulkLibrary';
import styles from './Library.module.css';

type MediaFilter = 'all' | 'series' | 'movies';

interface LibraryTileProps {
  to: string;
  id: number;
  kind: 'series' | 'movie';
  title: string;
  sortTitle: string;
  year: number;
  poster: React.ReactNode;
}

interface LibraryItem extends LibraryTileProps {
  key: string;
}

interface LibraryTileViewProps extends LibraryTileProps {
  isSelecting: boolean;
  isSelected: boolean;
  onToggle: (kind: 'series' | 'movie', id: number) => void;
}

function LibraryTile({
  to,
  id,
  kind,
  title,
  year,
  poster,
  isSelecting,
  isSelected,
  onToggle,
}: LibraryTileViewProps) {
  const handleToggle = useCallback(() => {
    onToggle(kind, id);
  }, [kind, id, onToggle]);

  if (isSelecting) {
    return (
      <label className={styles.tile}>
        <input
          type="checkbox"
          aria-label={title}
          checked={isSelected}
          onChange={handleToggle}
        />

        {poster}

        <div className={styles.title} title={title}>
          {title}
        </div>

        <div className={styles.meta}>{year || ''}</div>
      </label>
    );
  }

  return (
    <Link className={styles.tile} to={to}>
      {poster}

      <span className={styles.badge}>
        {kind === 'series' ? translate('Series') : translate('Movie')}
      </span>

      <div className={styles.title} title={title}>
        {title}
      </div>

      <div className={styles.meta}>{year || ''}</div>
    </Link>
  );
}

interface FilterButtonProps {
  value: MediaFilter;
  selected: MediaFilter;
  label: string;
  onSelect: (value: MediaFilter) => void;
}

function FilterButton({ value, selected, label, onSelect }: FilterButtonProps) {
  const handlePress = useCallback(() => {
    onSelect(value);
  }, [value, onSelect]);

  return (
    <Button
      kind={selected === value ? kinds.PRIMARY : kinds.DEFAULT}
      onPress={handlePress}
    >
      {label}
    </Button>
  );
}

// Series and movies together, the way the library is meant to be browsed. The full series
// management (filters, editing, seasons) stays on its own page.
function LibraryIndex() {
  const { data: series, isLoading: isLoadingSeries } = useSeries();
  const { data: movies, isLoading: isLoadingMovies } = useMovies();
  const [filter, setFilter] = useState<MediaFilter>('all');
  const profiles = useQualityProfilesData();
  const { applyToLibrary, isApplying, applyError } = useBulkLibrary();
  const [isSelecting, setIsSelecting] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);

  const selection = useMemo(
    () => ({
      seriesIds: series
        .filter((s) => selected.includes(`series-${s.id}`))
        .map((s) => s.id),
      movies: movies.filter((m) => selected.includes(`movie-${m.id}`)),
    }),
    [series, movies, selected]
  );

  const selectedCount = selection.seriesIds.length + selection.movies.length;

  const handleSelectToggle = useCallback(() => {
    setIsSelecting((value) => !value);
    setSelected([]);
  }, []);

  const handleTileToggle = useCallback(
    (kind: 'series' | 'movie', id: number) => {
      const key = `${kind}-${id}`;

      setSelected((keys) =>
        keys.includes(key) ? keys.filter((k) => k !== key) : [...keys, key]
      );
    },
    []
  );

  const handleSearchPress = useCallback(() => {
    applyToLibrary({ selection, action: { type: 'search' } });
  }, [applyToLibrary, selection]);

  const handleMonitorPress = useCallback(() => {
    applyToLibrary({ selection, action: { type: 'monitor', monitored: true } });
  }, [applyToLibrary, selection]);

  const handleUnmonitorPress = useCallback(() => {
    applyToLibrary({
      selection,
      action: { type: 'monitor', monitored: false },
    });
  }, [applyToLibrary, selection]);

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const qualityProfileId = Number(event.target.value);

      if (qualityProfileId) {
        applyToLibrary({
          selection,
          action: { type: 'profile', qualityProfileId },
        });
      }
    },
    [applyToLibrary, selection]
  );

  const handleDeletePress = useCallback(() => {
    setIsDeleteOpen(true);
  }, []);

  const handleDeleteCancel = useCallback(() => {
    setIsDeleteOpen(false);
  }, []);

  const handleDeleteConfirm = useCallback(() => {
    setIsDeleteOpen(false);
    applyToLibrary({ selection, action: { type: 'delete' } });
    setSelected([]);
  }, [applyToLibrary, selection]);

  const items = useMemo(() => {
    const result: LibraryItem[] = [];

    if (filter !== 'movies') {
      series.forEach((item) => {
        result.push({
          key: `series-${item.id}`,
          id: item.id,
          to: `/series/${item.titleSlug}`,
          kind: 'series',
          title: item.title,
          sortTitle: item.sortTitle ?? item.title,
          year: item.year,
          poster: (
            <SeriesPoster
              className={styles.poster}
              images={item.images}
              size={250}
              title={item.title}
            />
          ),
        });
      });
    }

    if (filter !== 'series') {
      movies.forEach((item) => {
        const poster = item.images.find(
          (image) => image.coverType === 'poster'
        );
        const url = poster?.url ?? poster?.remoteUrl;

        result.push({
          key: `movie-${item.id}`,
          id: item.id,
          to: `/movie/${item.id}`,
          kind: 'movie',
          title: item.title,
          sortTitle: item.sortTitle ?? item.title,
          year: item.year,
          poster: url ? (
            <img
              className={styles.poster}
              src={url}
              alt={item.title}
              loading="lazy"
            />
          ) : (
            <div className={styles.poster} />
          ),
        });
      });
    }

    return result.sort((a, b) => a.sortTitle.localeCompare(b.sortTitle));
  }, [series, movies, filter]);

  return (
    <PageContent title={translate('Library')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Media')}
          title={translate('Library')}
          actions={
            <>
              <Button onPress={handleSelectToggle}>
                {isSelecting ? translate('Cancel') : translate('Select')}
              </Button>
              <Button to="/add/new">{translate('AddNew')}</Button>
              <Button to="/add/movie" kind={kinds.PRIMARY}>
                {translate('AddMovie')}
              </Button>
            </>
          }
        />

        <div className={styles.filters}>
          <FilterButton
            value="all"
            selected={filter}
            label={translate('All')}
            onSelect={setFilter}
          />
          <FilterButton
            value="series"
            selected={filter}
            label={translate('Series')}
            onSelect={setFilter}
          />
          <FilterButton
            value="movies"
            selected={filter}
            label={translate('Movies')}
            onSelect={setFilter}
          />
        </div>

        {isSelecting && selectedCount ? (
          <div className={styles.filters}>
            <span>{selectedCount}</span>

            <Button isDisabled={isApplying} onPress={handleSearchPress}>
              {translate('Search')}
            </Button>

            <Button isDisabled={isApplying} onPress={handleMonitorPress}>
              {translate('Monitored')}
            </Button>

            <Button isDisabled={isApplying} onPress={handleUnmonitorPress}>
              {translate('Unmonitored')}
            </Button>

            <select
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

            <Button
              kind={kinds.DANGER}
              isDisabled={isApplying}
              onPress={handleDeletePress}
            >
              {translate('Delete')}
            </Button>

            {applyError ? <span>{applyError.message}</span> : null}
          </div>
        ) : null}

        <ConfirmModal
          isOpen={isDeleteOpen}
          kind={kinds.DANGER}
          title={translate('Delete')}
          message={translate('DeleteMovieHelpText')}
          confirmLabel={translate('Delete')}
          onConfirm={handleDeleteConfirm}
          onCancel={handleDeleteCancel}
        />

        {isLoadingSeries || isLoadingMovies ? <LoadingIndicator /> : null}

        <div className={styles.grid}>
          {items.map(({ key, ...tile }) => (
            <LibraryTile
              key={key}
              {...tile}
              isSelecting={isSelecting}
              isSelected={selected.includes(key)}
              onToggle={handleTileToggle}
            />
          ))}
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default LibraryIndex;
