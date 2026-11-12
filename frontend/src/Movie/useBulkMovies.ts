import { useMutation, useQueryClient } from '@tanstack/react-query';
import fetchJson, { ApiError } from 'Utilities/Fetch/fetchJson';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import Movie from './Movie';

export type BulkMovieAction =
  | { type: 'search' }
  | { type: 'monitor'; monitored: boolean }
  | { type: 'profile'; qualityProfileId: number }
  | { type: 'addTag'; tagId: number }
  | { type: 'removeTag'; tagId: number }
  | { type: 'delete'; deleteFiles: boolean };

interface BulkMovieVariables {
  movies: Movie[];
  action: BulkMovieAction;
}

const headers = () => ({
  'X-Api-Key': window.Sonarr.apiKey,
  'X-Sonarr-Client': 'Sonarr',
});

const getUpdatedMovie = (movie: Movie, action: BulkMovieAction) => {
  switch (action.type) {
    case 'monitor':
      return { ...movie, monitored: action.monitored };
    case 'addTag':
      return { ...movie, tags: [...new Set([...movie.tags, action.tagId])] };
    case 'removeTag':
      return { ...movie, tags: movie.tags.filter((t) => t !== action.tagId) };
    case 'profile':
      return { ...movie, qualityProfileId: action.qualityProfileId };
    default:
      return movie;
  }
};

// Applies one change to every selected movie with the ordinary single-movie calls.
const useBulkMovies = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useMutation<
    void,
    ApiError,
    BulkMovieVariables
  >({
    mutationFn: async ({ movies, action }) => {
      if (action.type === 'search') {
        await fetchJson({
          path: getQueryPath('/command'),
          method: 'POST',
          headers: headers(),
          body: { name: 'MoviesSearch', movieIds: movies.map((m) => m.id) },
        });

        return;
      }

      for (const movie of movies) {
        if (action.type === 'delete') {
          await fetchJson({
            path: getQueryPath(
              `/movie/${movie.id}?deleteFiles=${action.deleteFiles}`
            ),
            method: 'DELETE',
            headers: headers(),
          });
        } else {
          await fetchJson({
            path: getQueryPath('/movie'),
            method: 'PUT',
            headers: headers(),
            body: getUpdatedMovie(movie, action),
          });
        }
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['/movie'] });
    },
  });

  return { applyToMovies: mutate, isApplying: isPending, applyError: error };
};

export default useBulkMovies;
