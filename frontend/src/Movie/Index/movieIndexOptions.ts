import { useCallback, useState } from 'react';
import Movie from '../Movie';

export type MovieView = 'posters' | 'table';
export type MovieSortKey = 'sortTitle' | 'year' | 'added' | 'sizeOnDisk';
export type MovieFilterKey =
  | 'all'
  | 'monitored'
  | 'unmonitored'
  | 'missing'
  | 'downloaded';

const STORAGE_KEY = 'cinnabarr.movieIndex';

interface Stored {
  view: MovieView;
  sortKey: MovieSortKey;
  sortDirection: 'ascending' | 'descending';
  filterKey: MovieFilterKey;
}

const DEFAULTS: Stored = {
  view: 'posters',
  sortKey: 'sortTitle',
  sortDirection: 'ascending',
  filterKey: 'all',
};

const read = (): Stored => {
  try {
    return {
      ...DEFAULTS,
      ...JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '{}'),
    };
  } catch {
    return DEFAULTS;
  }
};

const write = (value: Stored) => {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
  } catch {
    // Remembering the choice is a convenience only.
  }
};

// The view, sort and filter of the Movies page, remembered in this browser.
export const useMovieIndexOptions = () => {
  const [options, setOptions] = useState<Stored>(read);

  const update = useCallback((change: Partial<Stored>) => {
    setOptions((current) => {
      const next = { ...current, ...change };
      write(next);
      return next;
    });
  }, []);

  const setView = useCallback(
    (view: string) => update({ view: view as MovieView }),
    [update]
  );
  const setFilter = useCallback(
    (key: string | number) => update({ filterKey: key as MovieFilterKey }),
    [update]
  );
  const setSort = useCallback(
    (key: string) =>
      setOptions((current) => {
        const next: Stored = {
          ...current,
          sortKey: key as MovieSortKey,
          sortDirection:
            current.sortKey === key && current.sortDirection === 'ascending'
              ? 'descending'
              : 'ascending',
        };
        write(next);
        return next;
      }),
    []
  );

  return { ...options, setView, setFilter, setSort };
};

export const filterMovies = (
  movies: readonly Movie[],
  filterKey: MovieFilterKey
) => {
  switch (filterKey) {
    case 'monitored':
      return movies.filter((m) => m.monitored);
    case 'unmonitored':
      return movies.filter((m) => !m.monitored);
    case 'missing':
      return movies.filter((m) => m.monitored && !m.hasFile);
    case 'downloaded':
      return movies.filter((m) => m.hasFile);
    default:
      return movies;
  }
};

export const sortMovies = (
  movies: readonly Movie[],
  sortKey: MovieSortKey,
  direction: 'ascending' | 'descending'
) => {
  const factor = direction === 'ascending' ? 1 : -1;
  const title = (m: Movie) => m.sortTitle ?? m.title.toLowerCase();

  return [...movies].sort((a, b) => {
    let result = 0;

    switch (sortKey) {
      case 'year':
        result = a.year - b.year;
        break;
      case 'added':
        result = a.added.localeCompare(b.added);
        break;
      case 'sizeOnDisk':
        result = a.sizeOnDisk - b.sizeOnDisk;
        break;
      default:
        result = title(a).localeCompare(title(b));
    }

    return result * factor || title(a).localeCompare(title(b));
  });
};
