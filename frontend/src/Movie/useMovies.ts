import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Movie from './Movie';

const DEFAULT_MOVIES: Movie[] = [];

export const useMovies = () => {
  const result = useApiQuery<Movie[]>({ path: '/movie' });

  return { ...result, data: result.data ?? DEFAULT_MOVIES };
};

export const useMovie = (id: number) => {
  return useApiQuery<Movie>({
    path: `/movie/${id}`,
    queryOptions: { enabled: id > 0 },
  });
};

export const useLookupMovies = (term: string) => {
  const result = useApiQuery<Movie[]>({
    path: '/movie/lookup',
    queryParams: { term },
    queryOptions: {
      enabled: !!term,
      // Not worth searching again just because the window got focus
      refetchOnWindowFocus: false,
    },
  });

  return { ...result, data: result.data ?? DEFAULT_MOVIES };
};

export interface AddMoviePayload {
  tmdbId: number;
  title: string;
  year: number;
  qualityProfileId: number;
  rootFolderPath: string;
  monitored: boolean;
  minimumAvailability: string;
  addOptions: { searchForMovie: boolean };
}

export const useAddMovie = () => {
  const queryClient = useQueryClient();

  const { isPending, error, mutate } = useApiMutation<Movie, AddMoviePayload>({
    path: '/movie',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/movie'] });
      },
    },
  });

  return { isAdding: isPending, addError: error, addMovie: mutate };
};

export const useUpdateMovie = () => {
  const queryClient = useQueryClient();

  const { isPending, error, mutate } = useApiMutation<Movie, Movie>({
    path: '/movie',
    method: 'PUT',
    getRequest: (movie) => ({ body: movie }),
    mutationOptions: {
      onSuccess: (movie) => {
        queryClient.setQueryData([`/movie/${movie.id}`], movie);
        queryClient.invalidateQueries({ queryKey: ['/movie'] });
      },
    },
  });

  return { isUpdating: isPending, updateError: error, updateMovie: mutate };
};

export const useDeleteMovie = (id: number) => {
  const queryClient = useQueryClient();

  const { isPending, error, mutate } = useApiMutation<
    unknown,
    void,
    { deleteFiles: boolean }
  >({
    path: `/movie/${id}`,
    method: 'DELETE',
    getRequest: ({ deleteFiles }) => ({ queryParams: { deleteFiles } }),
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/movie'] });
      },
    },
  });

  return { isDeleting: isPending, deleteError: error, deleteMovie: mutate };
};

export const useSearchMovie = () => {
  const { isPending, error, mutate } = useApiMutation<
    unknown,
    { name: string; movieIds: number[] }
  >({
    path: '/command',
    method: 'POST',
  });

  return {
    isSearching: isPending,
    searchError: error,
    searchMovie: (movieId: number) =>
      mutate({ name: 'MoviesSearch', movieIds: [movieId] }),
  };
};
