import { useMutation, useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

interface AddedTorrent {
  downloadId: string;
}

// Adds a magnet link to the engine by hand (roadmap §6.2); it shows in the queue as Unlinked.
export const useAddMagnet = () => {
  const queryClient = useQueryClient();

  return useApiMutation<AddedTorrent, { magnetLink: string }>({
    path: '/engine/torrents',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => queryClient.invalidateQueries({ queryKey: ['/queue'] }),
    },
  });
};

// Adds a .torrent file to the engine by hand.
export const useAddTorrentFile = () => {
  const queryClient = useQueryClient();

  return useMutation<AddedTorrent, Error, File>({
    mutationFn: async (file: File) => {
      const formData = new FormData();
      formData.append('file', file);

      const response = await fetch(getQueryPath('/engine/torrents/file'), {
        method: 'POST',
        headers: {
          'X-Api-Key': window.Sonarr.apiKey,
          'X-Sonarr-Client': 'Sonarr',
        },
        body: formData,
      });

      if (!response.ok) {
        const body = await response.json().catch(() => null);

        throw new Error(body?.message ?? response.statusText);
      }

      return response.json();
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['/queue'] }),
  });
};
