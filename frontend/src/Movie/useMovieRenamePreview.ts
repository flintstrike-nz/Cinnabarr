import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface MovieRenamePreview extends ModelBase {
  movieId: number;
  movieFileId: number;
  existingPath: string;
  newPath: string;
}

export interface MovieFolderPreview {
  movieId: number;
  existingPath: string;
  newPath: string;
}

export const useMovieFolderPreview = (movieId: number) => {
  return useApiQuery<MovieFolderPreview>({
    path: '/rename/movie/folder',
    queryParams: { movieId },
  });
};

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
