import { useQueryClient } from '@tanstack/react-query';
import ModelBase from 'App/ModelBase';
import useApiMutation, {
  addOrUpdateQueryClientItem,
} from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface UnmappedFolder {
  name: string;
  path: string;
  relativePath: string;

  // The series title in a release-like folder name, for looking it up.
  parsedTitle?: string;

  // The series in the library that the folder's name belongs to.
  matchedSeriesId?: number;
}

export interface RootFolder extends ModelBase {
  id: number;
  path: string;
  accessible: boolean;
  isEmpty: boolean;
  freeSpace?: number;
  unmappedFolders: UnmappedFolder[];

  // What the root folder holds: a series can't go under a movies root folder or the other way round.
  library: RootFolderLibrary;
}

export type RootFolderLibrary = 'any' | 'series' | 'movies';

interface AddRootFolder {
  path: string;
}

const DEFAULT_ROOT_FOLDERS: RootFolder[] = [];

const useRootFolders = () => {
  const result = useApiQuery<RootFolder[]>({
    path: '/rootFolder',
  });

  return {
    ...result,
    data: result.data ?? DEFAULT_ROOT_FOLDERS,
  };
};

// The root folders a kind of library can use: the ones that hold it and the ones that hold anything.
export const useLibraryRootFolders = (library: 'series' | 'movies') => {
  const result = useRootFolders();

  return {
    ...result,
    data: result.data.filter(
      (r) => r.library === 'any' || r.library === library
    ),
  };
};

export const useSetRootFolderLibrary = (rootFolder: RootFolder) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<unknown, RootFolder>({
    path: `/rootFolder/${rootFolder.id}`,
    method: 'PUT',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/rootFolder'] });
      },
    },
  });

  return {
    setLibrary: (library: RootFolderLibrary) =>
      mutate({ ...rootFolder, library }),
    isSaving: isPending,
    saveError: error,
  };
};

export const useRootFolder = (id: number, timeout: boolean) => {
  const result = useApiQuery<RootFolder>({
    path: `/rootFolder/${id}`,
    queryParams: { timeout },
    queryOptions: {
      // Disable refetch on window focus to prevent refetching when the user switch tabs
      refetchOnWindowFocus: false,
    },
  });

  return {
    ...result,
    data: result.data,
  };
};

export default useRootFolders;

export const useDeleteRootFolder = (id: number) => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error } = useApiMutation<unknown, void>({
    path: `/rootFolder/${id}`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/rootFolder'] });
      },
    },
  });

  return {
    deleteRootFolder: mutate,
    isDeleting: isPending,
    deleteError: error,
  };
};

export const useAddRootFolder = () => {
  const queryClient = useQueryClient();

  const { mutate, isPending, error, data } = useApiMutation<
    RootFolder,
    AddRootFolder
  >({
    path: '/rootFolder',
    method: 'POST',
    mutationOptions: {
      onSuccess: (newRootFolder) => {
        queryClient.setQueryData<RootFolder[]>(
          ['/rootFolder'],
          (oldRootFolders = []) =>
            addOrUpdateQueryClientItem(oldRootFolders, newRootFolder, 'id')
        );
      },
    },
  });

  return {
    addRootFolder: mutate,
    isAdding: isPending,
    addError: error,
    newRootFolder: data,
  };
};
