import { useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

// A version of a movie: the default one (its own profile, monitored state and file) or an extra, named one.
export interface VersionSlot {
  // The default version has no id in the response.
  id?: number;
  movieId: number;
  label: string;
  qualityProfileId: number;
  monitored: boolean;
  isDefault: boolean;
  movieFileId: number;
}

const NO_SLOTS: VersionSlot[] = [];

export const useVersionSlots = (movieId: number) => {
  const result = useApiQuery<VersionSlot[]>({
    path: '/versionslot',
    queryParams: { movieId },
    queryOptions: { enabled: movieId > 0 },
  });

  return { ...result, data: result.data ?? NO_SLOTS };
};

const useInvalidateSlots = () => {
  const queryClient = useQueryClient();

  return useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/versionslot'] });
    queryClient.invalidateQueries({ queryKey: ['/moviefile'] });
  }, [queryClient]);
};

export const useAddVersionSlot = () => {
  const invalidate = useInvalidateSlots();

  const { isPending, error, mutate } = useApiMutation<
    VersionSlot,
    Pick<VersionSlot, 'movieId' | 'label' | 'qualityProfileId' | 'monitored'>
  >({
    path: '/versionslot',
    method: 'POST',
    mutationOptions: { onSuccess: invalidate },
  });

  return { isAdding: isPending, addError: error, addSlot: mutate };
};

// Per version, like the other row-level mutations: the id is in the path.
export const useUpdateVersionSlot = (id: number) => {
  const invalidate = useInvalidateSlots();

  const { isPending, error, mutate } = useApiMutation<VersionSlot, VersionSlot>(
    {
      path: `/versionslot/${id}`,
      method: 'PUT',
      mutationOptions: { onSuccess: invalidate },
    }
  );

  return { isUpdating: isPending, updateError: error, updateSlot: mutate };
};

export const useDeleteVersionSlot = (id: number) => {
  const invalidate = useInvalidateSlots();

  const { isPending, error, mutate } = useApiMutation<unknown, void>({
    path: `/versionslot/${id}`,
    method: 'DELETE',
    mutationOptions: { onSuccess: invalidate },
  });

  return { isDeleting: isPending, deleteError: error, deleteSlot: mutate };
};
