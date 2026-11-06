import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Language from 'Language/Language';
import { QualityModel } from 'Quality/Quality';

export interface SearchResult {
  id: number;
  mediaType: 'series' | 'movie' | 'unknown';
  mappedSeriesId?: number;
  mappedMovieId?: number;
  mappedTitle?: string;
  release: {
    guid: string;
    title: string;
    indexerId: number;
    indexer: string;
    size: number;
    age: number;
    seeders?: number;
    leechers?: number;
  };
  decision: {
    approved: boolean;
    rejected: boolean;
    rejections: { message: string }[];
  };
  quality?: QualityModel;
  languages: Language[];
  downloadAllowed: boolean;
}

const NO_RESULTS: SearchResult[] = [];

// Searches every indexer, so it only runs for a term that was submitted.
export const useSearch = (term: string) => {
  const result = useApiQuery<SearchResult[]>({
    path: '/search',
    queryParams: { query: term },
    queryOptions: {
      enabled: term.length > 0,
      refetchOnWindowFocus: false,
      staleTime: 0,
    },
  });

  return { ...result, data: result.data ?? NO_RESULTS };
};

export const useGrabSearchResult = () => {
  const queryClient = useQueryClient();

  const { isPending, error, mutate } = useApiMutation<
    unknown,
    { guid: string; indexerId: number }
  >({
    path: '/search/grab',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/queue'] });
      },
    },
  });

  return { isGrabbing: isPending, grabError: error, grab: mutate };
};
