import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Movie from './Movie';

interface PagedMovies {
  page: number;
  pageSize: number;
  totalRecords: number;
  records: Movie[];
}

const DEFAULT_MOVIES: Movie[] = [];

export type WantedKind = 'missing' | 'cutoff';

// The first page of the wanted movies; the lists are short enough not to need paging controls yet.
export const useWantedMovies = (kind: WantedKind) => {
  const result = useApiQuery<PagedMovies>({
    path: `/wanted/movie/${kind}`,
    queryParams: {
      page: 1,
      pageSize: 100,
      sortKey: 'movieMetadata.sortTitle',
      sortDirection: 'ascending',
      monitored: true,
    },
  });

  return {
    ...result,
    totalRecords: result.data?.totalRecords ?? 0,
    data: result.data?.records ?? DEFAULT_MOVIES,
  };
};

export const useUpcomingMovies = (days: number) => {
  const start = new Date();
  const end = new Date(start.getTime() + days * 24 * 60 * 60 * 1000);

  const result = useApiQuery<Movie[]>({
    path: '/calendar/movie',
    queryParams: {
      start: start.toISOString().slice(0, 10),
      end: end.toISOString().slice(0, 10),
      includeUnmonitored: false,
    },
  });

  return { ...result, data: result.data ?? DEFAULT_MOVIES };
};

export const useSearchWantedMovies = () => {
  const { isPending, mutate } = useApiMutation<unknown, { name: string }>({
    path: '/command',
    method: 'POST',
  });

  return {
    isSearching: isPending,
    searchWanted: (kind: WantedKind) =>
      mutate({
        name:
          kind === 'missing'
            ? 'MissingMoviesSearch'
            : 'CutoffUnmetMoviesSearch',
      }),
  };
};

export interface WantedMovieVersion {
  movieId: number;
  movieTitle?: string;
  year: number;
  versionSlotId: number;
  versionLabel?: string;
  qualityProfileId: number;
}

const NO_VERSIONS: WantedMovieVersion[] = [];

// The wanted versions of movies other than the movie's own (default) version.
export const useWantedMovieVersions = (kind: WantedKind) => {
  const result = useApiQuery<WantedMovieVersion[]>({
    path: `/wanted/movie/${kind}/versions`,
  });

  return { ...result, data: result.data ?? NO_VERSIONS };
};
