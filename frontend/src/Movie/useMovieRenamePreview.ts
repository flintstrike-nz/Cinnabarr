import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface MovieRenamePreview extends ModelBase {
  movieId: number;
  movieFileId: number;
  existingPath: string;
  newPath: string;
}

const DEFAULT_PREVIEWS: MovieRenamePreview[] = [];

const useMovieRenamePreview = (movieId: number) => {
  const { data, ...result } = useApiQuery<MovieRenamePreview[]>({
    path: '/rename/movie',
    queryParams: { movieIds: movieId },
  });

  return {
    items: data ?? DEFAULT_PREVIEWS,
    ...result,
  };
};

export default useMovieRenamePreview;
