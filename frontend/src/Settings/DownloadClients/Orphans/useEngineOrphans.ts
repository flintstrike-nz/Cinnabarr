import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface EngineOrphanFile {
  path: string;
  size: number;
  lastWrite: string;
}

export interface EngineOrphans {
  graceDays: number;
  deleteAutomatically: boolean;
  files: EngineOrphanFile[];
}

const PATH = '/engine/orphans';
const SETTINGS_PATH = '/settings/downloadclient';

// Scans for leftover partial files (roadmap §4.13); nothing is deleted by looking.
export const useEngineOrphans = () => {
  return useApiQuery<EngineOrphans>({
    path: PATH,
    queryOptions: { gcTime: 0, refetchOnWindowFocus: false },
  });
};

// Deletes the listed files that are still orphans and sets whether the daily cleanup deletes them itself.
export const useApproveEngineOrphans = () => {
  const queryClient = useQueryClient();

  return useApiMutation<
    EngineOrphans,
    { paths: string[]; deleteAutomatically: boolean }
  >({
    path: `${PATH}/delete`,
    method: 'POST',
    mutationOptions: {
      // The approval changes the automatic-deletion setting too.
      onSuccess: () =>
        Promise.all([
          queryClient.invalidateQueries({ queryKey: [PATH] }),
          queryClient.invalidateQueries({ queryKey: [SETTINGS_PATH] }),
        ]),
    },
  });
};
