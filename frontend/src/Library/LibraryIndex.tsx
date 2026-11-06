import React, { useCallback, useMemo, useState } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { kinds } from 'Helpers/Props';
import { useMovies } from 'Movie/useMovies';
import SeriesPoster from 'Series/SeriesPoster';
import useSeries from 'Series/useSeries';
import translate from 'Utilities/String/translate';
import styles from './Library.module.css';

type MediaFilter = 'all' | 'series' | 'movies';

interface LibraryTileProps {
  to: string;
  kind: 'series' | 'movie';
  title: string;
  sortTitle: string;
  year: number;
  poster: React.ReactNode;
}

interface LibraryItem extends LibraryTileProps {
  key: string;
}

function LibraryTile({ to, kind, title, year, poster }: LibraryTileProps) {
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

  const items = useMemo(() => {
    const result: LibraryItem[] = [];

    if (filter !== 'movies') {
      series.forEach((item) => {
        result.push({
          key: `series-${item.id}`,
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

        {isLoadingSeries || isLoadingMovies ? <LoadingIndicator /> : null}

        <div className={styles.grid}>
          {items.map(({ key, ...tile }) => (
            <LibraryTile key={key} {...tile} />
          ))}
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default LibraryIndex;
