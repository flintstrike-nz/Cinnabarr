import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import Command from 'Commands/Command';
import { useExecuteCommand } from 'Commands/useCommands';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import {
  addMovieMergeReport,
  MovieMergeResult,
  setImportMovieStatus,
  UnmappedMovieFolder,
} from './importMoviesStore';
import Movie from './Movie';
import { AddMoviePayload } from './useMovies';

interface ImportOptions {
  rootFolderPath: string;
  qualityProfileId: number;
}

// What the Import Movies page does with a row: add its match as a new movie (a folder becomes the movie's folder; a
// loose file is moved into a new folder for it), or merge it into the movie the library already has.
export const useImportMovieActions = () => {
  const queryClient = useQueryClient();
  const executeCommand = useExecuteCommand();

  const { mutateAsync: addMovie } = useApiMutation<Movie, AddMoviePayload>({
    path: '/movie',
    method: 'POST',
  });

  const refresh = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/movie'] });
    queryClient.invalidateQueries({ queryKey: ['/movie/import'] });
  }, [queryClient]);

  const merge = useCallback(
    (movie: Pick<Movie, 'id' | 'title'>, paths: string[]) => {
      paths.forEach((path) => setImportMovieStatus(path, { state: 'working' }));

      executeCommand(
        { name: 'MergeIntoMovie', movieId: movie.id, paths },
        (command: Command) => {
          const { results = [] } = command.body as unknown as {
            results?: MovieMergeResult[];
          };

          paths.forEach((path) => {
            const left = results.filter(
              (r) => !r.merged && (r.path === path || r.path.startsWith(path))
            );

            setImportMovieStatus(
              path,
              command.status === 'completed' && !left.length
                ? { state: 'done' }
                : {
                    state: 'failed',
                    message: left.map((r) => r.reason).join('; ') || undefined,
                  }
            );
          });

          if (results.length) {
            addMovieMergeReport({ movieTitle: movie.title, results });
          }

          refresh();
        }
      );
    },
    [executeCommand, refresh]
  );

  const importNew = useCallback(
    async (
      folder: UnmappedMovieFolder,
      match: Movie,
      { rootFolderPath, qualityProfileId }: ImportOptions
    ) => {
      setImportMovieStatus(folder.path, { state: 'working' });

      try {
        const movie = await addMovie({
          tmdbId: match.tmdbId,
          title: match.title,
          year: match.year,
          qualityProfileId,
          monitored: true,
          minimumAvailability: 'released',
          addOptions: { searchForMovie: false },
          ...(folder.isFile ? { rootFolderPath } : { path: folder.path }),
        });

        if (folder.isFile) {
          merge(movie, [folder.path]);
        } else {
          setImportMovieStatus(folder.path, { state: 'done' });
          refresh();
        }
      } catch (error) {
        setImportMovieStatus(folder.path, {
          state: 'failed',
          message: getErrorMessage(error as ApiError, String(error)),
        });
      }
    },
    [addMovie, merge, refresh]
  );

  return { importNew, merge };
};
