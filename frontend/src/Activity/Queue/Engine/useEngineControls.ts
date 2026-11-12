import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { ApiError } from 'Utilities/Fetch/fetchJson';

export interface EngineStatus {
  downloadSpeed: number;
  uploadSpeed: number;
  alternativeSpeedLimits: boolean;
}

const STATUS_PATH = '/engine/status';

export function getEngineErrorMessage(error: ApiError | null) {
  return error ? error.statusBody?.message ?? error.message : undefined;
}

// Live engine speeds and the alternative speed limits, refreshed every few seconds while shown (roadmap §6.2).
export const useEngineStatus = () => {
  return useApiQuery<EngineStatus>({
    path: STATUS_PATH,
    queryOptions: {
      // Slower while the engine isn't answering.
      refetchInterval: (query) => (query.state.error ? 30000 : 3000),
      retry: false,
    },
  });
};

export const useSetAlternativeSpeedLimits = () => {
  const queryClient = useQueryClient();

  return useApiMutation<void, void, boolean>({
    path: '/engine/altspeed',
    method: 'PUT',
    getRequest: (enabled) => ({ queryParams: { enabled } }),
    mutationOptions: {
      onSettled: () =>
        queryClient.invalidateQueries({ queryKey: [STATUS_PATH] }),
    },
  });
};

// Pauses or resumes every torrent; resume leaves torrents still being prepared alone.
export const useEngineAll = (action: 'pause' | 'resume') => {
  const queryClient = useQueryClient();

  return useApiMutation<void, void>({
    path: `/engine/${action}`,
    method: 'POST',
    mutationOptions: {
      onSettled: () => queryClient.invalidateQueries({ queryKey: ['/queue'] }),
    },
  });
};

export type EngineDownloadAction = 'pause' | 'resume' | 'recheck';

// A control on one engine download; afterwards the queue and its files are refreshed.
export const useEngineDownloadAction = (
  downloadId: string,
  action: EngineDownloadAction
) => {
  const queryClient = useQueryClient();

  return useApiMutation<void, void>({
    path: `/engine/downloads/${downloadId}/${action}`,
    method: 'POST',
    mutationOptions: {
      onSettled: () =>
        Promise.all([
          queryClient.invalidateQueries({ queryKey: ['/queue'] }),
          queryClient.invalidateQueries({
            queryKey: [`/engine/downloads/${downloadId}/files`],
          }),
        ]),
    },
  });
};

export type EngineQueueMove = 'top' | 'up' | 'down' | 'bottom';

const useRefreshDownload = (downloadId: string) => {
  const queryClient = useQueryClient();

  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['/queue'] }),
      queryClient.invalidateQueries({
        queryKey: [`/engine/downloads/${downloadId}/files`],
      }),
    ]);
};

// Moves one engine download in the engine's queue.
export const useEngineQueueMove = (downloadId: string) => {
  const refresh = useRefreshDownload(downloadId);

  return useApiMutation<void, void, EngineQueueMove>({
    path: `/engine/downloads/${downloadId}/queue`,
    method: 'POST',
    getRequest: (move) => ({ queryParams: { move } }),
    mutationOptions: { onSettled: refresh },
  });
};

// Sets one engine download's own speed limits, in KiB/s (0 is unlimited).
export const useEngineSpeedLimits = (downloadId: string) => {
  const refresh = useRefreshDownload(downloadId);

  return useApiMutation<void, void, { download: number; upload: number }>({
    path: `/engine/downloads/${downloadId}/limits`,
    method: 'PUT',
    getRequest: (limits) => ({ queryParams: limits }),
    mutationOptions: { onSettled: refresh },
  });
};

// Turns force start on or off for one engine download.
export const useEngineForceStart = (downloadId: string) => {
  const queryClient = useQueryClient();

  return useApiMutation<void, void, boolean>({
    path: `/engine/downloads/${downloadId}/forcestart`,
    method: 'POST',
    getRequest: (enabled) => ({ queryParams: { enabled } }),
    mutationOptions: {
      onSettled: () =>
        Promise.all([
          queryClient.invalidateQueries({ queryKey: ['/queue'] }),
          queryClient.invalidateQueries({
            queryKey: [`/engine/downloads/${downloadId}/files`],
          }),
        ]),
    },
  });
};
