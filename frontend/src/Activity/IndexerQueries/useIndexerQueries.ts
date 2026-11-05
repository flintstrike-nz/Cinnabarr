import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface IndexerQuery {
  id: number;
  indexerId: number;
  indexerName?: string;
  date: string;
  source: 'rss' | 'search';
  query?: string;
  successful: boolean;
  resultCount: number;
  elapsedMs: number;
  error?: string;
}

export interface IndexerQueryStats {
  indexerId: number;
  indexerName?: string;
  queries: number;
  failures: number;
  averageElapsedMs: number;
  lastQuery: string;
}

export const useIndexerQueries = () => {
  const result = useApiQuery<IndexerQuery[]>({
    path: '/indexerquery',
    queryParams: { limit: 200 },
    queryOptions: { refetchInterval: 30000 },
  });

  return { ...result, data: result.data ?? [] };
};

export const useIndexerQueryStats = () => {
  const result = useApiQuery<IndexerQueryStats[]>({
    path: '/indexerquery/stats',
    queryParams: { days: 7 },
    queryOptions: { refetchInterval: 30000 },
  });

  return { ...result, data: result.data ?? [] };
};
