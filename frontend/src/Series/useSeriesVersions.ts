import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

// A version of a series: the default one (its own profile, monitored state and files) or an extra, named one.
export interface SeriesVersion {
  // The default version has no id in the response.
  id?: number;
  seriesId: number;
  label: string;
  qualityProfileId: number;
  monitored: boolean;
  isDefault: boolean;
  episodeFileCount: number;
}

const NO_VERSIONS: SeriesVersion[] = [];

export const useSeriesVersions = (seriesId: number) => {
  const result = useApiQuery<SeriesVersion[]>({
    path: '/seriesversion',
    queryParams: { seriesId },
    queryOptions: { enabled: seriesId > 0 },
  });

  return { ...result, data: result.data ?? NO_VERSIONS };
};

const useInvalidate = () => {
  const queryClient = useQueryClient();

  return useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/seriesversion'] });
    queryClient.invalidateQueries({ queryKey: ['/episodefile'] });
  }, [queryClient]);
};

export const useAddSeriesVersion = () => {
  const invalidate = useInvalidate();

  const { isPending, error, mutate } = useApiMutation<
    SeriesVersion,
    Pick<SeriesVersion, 'seriesId' | 'label' | 'qualityProfileId' | 'monitored'>
  >({
    path: '/seriesversion',
    method: 'POST',
    mutationOptions: { onSuccess: invalidate },
  });

  return { isAdding: isPending, addError: error, addVersion: mutate };
};

export const useUpdateSeriesVersion = (id: number) => {
  const invalidate = useInvalidate();

  const { isPending, error, mutate } = useApiMutation<
    SeriesVersion,
    SeriesVersion
  >({
    path: `/seriesversion/${id}`,
    method: 'PUT',
    mutationOptions: { onSuccess: invalidate },
  });

  return { isUpdating: isPending, updateError: error, updateVersion: mutate };
};

export const useDeleteSeriesVersion = (id: number) => {
  const invalidate = useInvalidate();

  const { isPending, error, mutate } = useApiMutation<unknown, void>({
    path: `/seriesversion/${id}`,
    method: 'DELETE',
    mutationOptions: { onSuccess: invalidate },
  });

  return { isDeleting: isPending, deleteError: error, deleteVersion: mutate };
};

// Searches indexers for the monitored missing episodes of one of the series' extra versions.
export const useSearchSeriesVersion = () => {
  const { isPending, error, mutate } = useApiMutation<
    unknown,
    { name: string; seriesId: number; versionSlotId: number }
  >({
    path: '/command',
    method: 'POST',
  });

  return {
    isSearching: isPending,
    searchError: error,
    searchVersion: (seriesId: number, versionSlotId: number) =>
      mutate({ name: 'SeriesSearch', seriesId, versionSlotId }),
  };
};
