import { create } from 'zustand';
import Movie from './Movie';

export interface UnmappedMovieFolder {
  name: string;
  path: string;
  parsedTitle: string;
  parsedYear: number;
  isFile: boolean;
}

export type ImportMovieEligibility =
  | 'searching'
  | 'unmatched'
  | 'existing'
  | 'duplicate'
  | 'ready';

export type ImportMovieState = 'working' | 'done' | 'failed';

export interface ImportMovieStatus {
  state: ImportMovieState;
  message?: string;
}

export interface MovieMergeResult {
  path: string;
  merged: boolean;
  reason?: string;
}

export interface MovieMergeReport {
  movieTitle: string;
  results: MovieMergeResult[];
}

interface ImportMoviesState {
  // The lookup result each row is matched to (undefined while it is still being looked up, null when none).
  matches: Record<string, Movie | null | undefined>;
  selected: Record<string, boolean>;
  statuses: Record<string, ImportMovieStatus>;
  reports: MovieMergeReport[];
}

const INITIAL: ImportMoviesState = {
  matches: {},
  selected: {},
  statuses: {},
  reports: [],
};

// The Import Movies page's working state: each row's match, whether it is selected, how its import went, and
// the reports of finished merges.
export const importMoviesStore = create<ImportMoviesState>(() => INITIAL);

export const useImportMoviesState = <T>(
  selector: (state: ImportMoviesState) => T
) => importMoviesStore(selector);

export const clearImportMovies = () => importMoviesStore.setState(INITIAL);

export const setImportMovieMatch = (
  path: string,
  match: Movie | null | undefined
) => {
  importMoviesStore.setState((state) => ({
    matches: { ...state.matches, [path]: match },
  }));
};

export const setImportMoviesSelected = (paths: string[], value: boolean) => {
  importMoviesStore.setState((state) => {
    const selected = { ...state.selected };

    paths.forEach((path) => {
      selected[path] = value;
    });

    return { selected };
  });
};

export const setImportMovieStatus = (
  path: string,
  status: ImportMovieStatus
) => {
  importMoviesStore.setState((state) => ({
    statuses: { ...state.statuses, [path]: status },
  }));
};

export const addMovieMergeReport = (report: MovieMergeReport) => {
  importMoviesStore.setState((state) => ({
    reports: [...state.reports, report],
  }));
};

export const clearMovieMergeReports = () => {
  importMoviesStore.setState({ reports: [] });
};

// Whether each row can be imported: it needs a match, one not in the library yet and not also chosen by an
// earlier row (a later duplicate can still be picked instead by selecting it).
export const getImportMovieEligibility = (
  folders: UnmappedMovieFolder[],
  matches: ImportMoviesState['matches'],
  selected: ImportMoviesState['selected'],
  libraryTmdbIds: Set<number>
) => {
  const eligibility = new Map<string, ImportMovieEligibility>();
  const claimed = new Map<number, string>();

  // A selected row claims its movie first, then the first row in the list.
  const ordered = [
    ...folders.filter((f) => selected[f.path]),
    ...folders.filter((f) => !selected[f.path]),
  ];

  ordered.forEach((folder) => {
    const match = matches[folder.path];

    if (match === undefined) {
      eligibility.set(folder.path, 'searching');
    } else if (match === null) {
      eligibility.set(folder.path, 'unmatched');
    } else if (libraryTmdbIds.has(match.tmdbId)) {
      eligibility.set(folder.path, 'existing');
    } else if (claimed.has(match.tmdbId)) {
      eligibility.set(folder.path, 'duplicate');
    } else {
      claimed.set(match.tmdbId, folder.path);
      eligibility.set(folder.path, 'ready');
    }
  });

  return eligibility;
};
