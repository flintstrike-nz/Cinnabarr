import { useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface SeedingRule {
  mode: number;
  minutes: number;
  ratio: number;
}

export interface SeriesSeedingRules {
  seriesId: number;
  rule: SeedingRule | null;
  seasons: { seasonNumber: number; rule: SeedingRule | null }[];
  episodes: {
    episodeId: number;
    seasonNumber: number;
    episodeNumber: number;
    rule: SeedingRule | null;
  }[];
}

const getPath = (seriesId: number) => `/seeding/series/${seriesId}`;

export const useSeriesSeedingRules = (seriesId: number) => {
  return useApiQuery<SeriesSeedingRules>({ path: getPath(seriesId) });
};

// Sets (or, with null, clears) a series' or season's seeding goal. Without a season number, the series' own.
export const useSaveSeedingRule = (seriesId: number, seasonNumber?: number) => {
  const queryClient = useQueryClient();

  const path = useMemo(
    () =>
      seasonNumber === undefined
        ? getPath(seriesId)
        : `${getPath(seriesId)}/season/${seasonNumber}`,
    [seriesId, seasonNumber]
  );

  return useApiMutation<void, { rule: SeedingRule | null }>({
    path,
    method: 'PUT',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: [getPath(seriesId)] });
      },
    },
  });
};
