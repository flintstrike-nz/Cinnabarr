import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Language from 'Language/Language';
import { QualityModel } from 'Quality/Quality';

export interface MovieRelease {
  id: number;
  guid?: string;
  parsedInfo?: { quality?: QualityModel; releaseGroup?: string };
  release?: {
    guid: string;
    title: string;
    indexerId: number;
    indexer: string;
    size: number;
    age: number;
    ageHours: number;
    protocol: string;
    seeders?: number;
    leechers?: number;
  };
  decision?: {
    approved: boolean;
    rejected: boolean;
    temporarilyRejected: boolean;
    rejections: { message: string }[];
  };
  languages: Language[];
  customFormatScore: number;
  qualityWeight: number;
  releaseWeight: number;
}

const DEFAULT_RELEASES: MovieRelease[] = [];

// An interactive search: only runs once asked for, as it queries every indexer.
export const useMovieReleases = (
  movieId: number,
  isEnabled: boolean,
  versionSlotId = 0
) => {
  const result = useApiQuery<MovieRelease[]>({
    path: '/release/movie',
    queryParams: { movieId, versionSlotId },
    queryOptions: {
      enabled: isEnabled && movieId > 0,
      refetchOnWindowFocus: false,
      staleTime: 0,
    },
  });

  return { ...result, data: result.data ?? DEFAULT_RELEASES };
};

export const useGrabMovieRelease = () => {
  const queryClient = useQueryClient();

  const { isPending, error, mutate } = useApiMutation<
    unknown,
    { guid: string; indexerId: number }
  >({
    path: '/release/movie',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
    },
  });

  return { isGrabbing: isPending, grabError: error, grabRelease: mutate };
};
