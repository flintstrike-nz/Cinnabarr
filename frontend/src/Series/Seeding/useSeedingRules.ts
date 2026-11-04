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

// The API writes modes as names ("ratio"); the UI works with EngineSeedingMode's numbers, as the global setting does.
const modeNumbers: Record<string, number> = {
  none: 0,
  time: 1,
  ratio: 2,
  either: 3,
  both: 4,
};

function normalizeRule(rule: SeedingRule | null): SeedingRule | null {
  if (!rule) {
    return null;
  }

  const mode: unknown = rule.mode;

  return {
    ...rule,
    mode:
      typeof mode === 'string'
        ? modeNumbers[mode.toLowerCase()] ?? 0
        : rule.mode,
  };
}

function normalizeRules(rules: SeriesSeedingRules): SeriesSeedingRules {
  return {
    ...rules,
    rule: normalizeRule(rules.rule),
    seasons: rules.seasons.map((s) => ({ ...s, rule: normalizeRule(s.rule) })),
    episodes: rules.episodes.map((e) => ({
      ...e,
      rule: normalizeRule(e.rule),
    })),
  };
}

export const useSeriesSeedingRules = (seriesId: number) => {
  return useApiQuery<SeriesSeedingRules>({
    path: getPath(seriesId),
    queryOptions: { select: normalizeRules },
  });
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
