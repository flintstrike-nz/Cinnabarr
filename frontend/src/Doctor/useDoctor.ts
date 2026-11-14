import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

export interface DoctorFile {
  id: number;
  path?: string;
  size: number;
  quality?: string;
  resolution: number;
  videoBitrate: number;
  videoCodec?: string;
  dateAdded: string;
}

export interface DoctorFlag {
  id: number;
  mediaType: 'series' | 'movie';
  itemId: number;
  title?: string;
  reason?: string;
  flaggedAt: string;
  deleteAfter: string;
  flagged?: DoctorFile;
  kept?: DoctorFile;
}

export interface DoctorSettings {
  enabled: boolean;
  intervalHours: number;
  retentionDays: number;
}

const NO_FLAGS: DoctorFlag[] = [];

export const useDoctorFlags = () => {
  const result = useApiQuery<DoctorFlag[]>({
    path: '/doctor',
    queryOptions: { refetchInterval: 60000 },
  });

  return { ...result, data: result.data ?? NO_FLAGS };
};

export const useDoctorSettings = () => {
  const queryClient = useQueryClient();
  const query = useApiQuery<DoctorSettings>({ path: '/doctor/settings' });

  const { mutate, isPending, error } = useApiMutation<
    DoctorSettings,
    DoctorSettings
  >({
    path: '/doctor/settings',
    method: 'PUT',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/doctor/settings'] });
      },
    },
  });

  return {
    settings: query.data,
    save: mutate,
    isSaving: isPending,
    saveError: error,
  };
};

const useInvalidate = () => {
  const queryClient = useQueryClient();

  return useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/doctor'] });
  }, [queryClient]);
};

export const useDeleteFlag = (id: number) => {
  const invalidate = useInvalidate();

  const { mutate, isPending, error } = useApiMutation<unknown, void>({
    path: `/doctor/${id}`,
    method: 'DELETE',
    mutationOptions: { onSuccess: invalidate },
  });

  return { deleteFlag: () => mutate(), isDeleting: isPending, error };
};

export const useRecoverFlag = (id: number) => {
  const invalidate = useInvalidate();

  const { mutate, isPending, error } = useApiMutation<unknown, boolean>({
    path: `/doctor/${id}/recover`,
    method: 'POST',
    getRequest: (keepBoth) => ({ queryParams: { keepBoth } }),
    mutationOptions: { onSuccess: invalidate },
  });

  return { recover: mutate, isRecovering: isPending, error };
};

// An image of the file. The api key goes in the query because an <img> cannot send headers.
export const getScreenshotUrl = (
  mediaType: string,
  fileId: number,
  index: number
) =>
  `${getQueryPath(
    `/doctor/screenshot/${mediaType}/${fileId}/${index}`
  )}?apikey=${window.Sonarr.apiKey}`;
