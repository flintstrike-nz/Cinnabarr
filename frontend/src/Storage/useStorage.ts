import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface StorageDrive {
  path: string;
  totalSpace: number;
  freeSpace: number;
  incomingBytes: number;
}

export interface StorageMove {
  mediaType: 'series' | 'movie';
  id: number;
  title?: string;
  size: number;
  fromDrive: string;
  toDrive: string;
}

export interface StorageLibrary {
  library: 'series' | 'movies';
  drives: StorageDrive[];
  moves: StorageMove[];
}

export interface StorageSettings {
  enabled: boolean;
  intervalHours: number;
  tolerancePercent: number;
  reservePercent: number;
  maxMoveGb: number;
  forecastDays: number;
}

const NO_LIBRARIES: StorageLibrary[] = [];

export const useStoragePlan = () => {
  const result = useApiQuery<StorageLibrary[]>({
    path: '/storage/plan',
    queryOptions: { refetchOnWindowFocus: false },
  });

  return { ...result, data: result.data ?? NO_LIBRARIES };
};

export const useStorageSettings = () => {
  const queryClient = useQueryClient();
  const query = useApiQuery<StorageSettings>({ path: '/storage/settings' });

  const { mutate, isPending, error } = useApiMutation<
    StorageSettings,
    StorageSettings
  >({
    path: '/storage/settings',
    method: 'PUT',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/storage/settings'] });
        queryClient.invalidateQueries({ queryKey: ['/storage/plan'] });
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
