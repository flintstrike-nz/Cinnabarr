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
const STATUS_PATH = '/engine/orphans/status';

export interface EngineOrphanStatus {
  graceDays: number;
  deleteAutomatically: boolean;
  awaitingReview: number;
}

// Scans for leftover partial files (roadmap §4.13); nothing is deleted by looking.
export const useEngineOrphans = () => {
  return useApiQuery<EngineOrphans>({
    path: PATH,
    queryOptions: { gcTime: 0, refetchOnWindowFocus: false },
  });
};

// Whether the daily cleanup deletes leftover files itself; doesn't scan.
export const useEngineOrphanStatus = () => {
  return useApiQuery<EngineOrphanStatus>({ path: STATUS_PATH });
};

// Turns automatic deletion off. Only an approval (useApproveEngineOrphans) turns it on.
export const useStopEngineOrphanCleanup = () => {
  const queryClient = useQueryClient();

  return useApiMutation<EngineOrphanStatus, void>({
    path: `${PATH}/stop`,
    method: 'POST',
    mutationOptions: {
      onSettled: () =>
        queryClient.invalidateQueries({ queryKey: [STATUS_PATH] }),
    },
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
      // The approval sets automatic deletion too, which can be saved even when the request then fails (the rescan
      // after deleting can), so the state is refreshed either way.
      onSettled: () =>
        Promise.all([
          queryClient.invalidateQueries({ queryKey: [PATH] }),
          queryClient.invalidateQueries({ queryKey: [STATUS_PATH] }),
        ]),
    },
  });
};
