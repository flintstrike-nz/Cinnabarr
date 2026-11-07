import { useMutation, useQueryClient } from '@tanstack/react-query';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

// A library tile is a series or a movie; a selection can hold both.
export interface LibrarySelection {
  seriesIds: number[];
  movies: { id: number }[];
}

export type BulkLibraryAction =
  | { type: 'search' }
  | { type: 'monitor'; monitored: boolean }
  | { type: 'profile'; qualityProfileId: number }
  | { type: 'delete' };

interface BulkLibraryVariables {
  selection: LibrarySelection;
  action: BulkLibraryAction;
}

const headers = () => ({
  'X-Api-Key': window.Sonarr.apiKey,
  'X-Sonarr-Client': 'Sonarr',
});

const call = (
  path: string,
  method: 'POST' | 'PUT' | 'DELETE',
  body?: unknown
) => fetchJson({ path: getQueryPath(path), method, headers: headers(), body });

// Applies one change to every selected series and movie: the series editor for the series, the single-movie calls for
// the movies. Deleting never deletes files.
const useBulkLibrary = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useMutation<
    void,
    ApiError,
    BulkLibraryVariables
  >({
    mutationFn: async ({ selection, action }) => {
      const { seriesIds, movies } = selection;

      if (action.type === 'search') {
        for (const seriesId of seriesIds) {
          await call('/command', 'POST', { name: 'SeriesSearch', seriesId });
        }

        if (movies.length) {
          await call('/command', 'POST', {
            name: 'MoviesSearch',
            movieIds: movies.map((m) => m.id),
          });
        }

        return;
      }

      if (action.type === 'delete') {
        if (seriesIds.length) {
          await call('/series/editor', 'DELETE', {
            seriesIds,
            deleteFiles: false,
          });
        }

        for (const movie of movies) {
          await call(`/movie/${movie.id}?deleteFiles=false`, 'DELETE');
        }

        return;
      }

      if (seriesIds.length) {
        await call('/series/editor', 'PUT', {
          seriesIds,
          ...(action.type === 'monitor'
            ? { monitored: action.monitored }
            : { qualityProfileId: action.qualityProfileId }),
        });
      }

      for (const movie of movies) {
        const current = await fetchJson<Record<string, unknown>, unknown>({
          path: getQueryPath(`/movie/${movie.id}`),
          method: 'GET',
          headers: headers(),
        });

        await call('/movie', 'PUT', {
          ...current,
          ...(action.type === 'monitor'
            ? { monitored: action.monitored }
            : { qualityProfileId: action.qualityProfileId }),
        });
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['/movie'] });
      queryClient.invalidateQueries({ queryKey: ['/series'] });
    },
  });

  return { applyToLibrary: mutate, isApplying: isPending, applyError: error };
};

export default useBulkLibrary;
