import { useQueryClient } from '@tanstack/react-query';
import { useMemo } from 'react';
import ModelBase from 'App/ModelBase';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export type FileOperationKind = 'import' | 'move' | 'copy' | 'rename';

export type FileOperationStatus =
  | 'queued'
  | 'running'
  | 'completed'
  | 'skipped'
  | 'failed';

export interface FileOperation extends ModelBase {
  runIndex: number;
  kind: FileOperationKind;
  title?: string;
  path: string;
  destination?: string;
  status: FileOperationStatus;
  progress: number;
  size: number;
  message?: string;
  queued: string;
  started?: string;
  ended?: string;
}

const PATH = '/fileoperation';

export const isFinished = (operation: FileOperation) =>
  operation.status !== 'queued' && operation.status !== 'running';

// The file operations queue, in run order. SignalR keeps it current, so it is only fetched once.
export const useFileOperations = () => {
  const { data, ...result } = useApiQuery<FileOperation[]>({
    path: PATH,
    queryOptions: { staleTime: Infinity },
  });

  const operations = useMemo(
    () => [...(data ?? [])].sort((a, b) => a.runIndex - b.runIndex),
    [data]
  );

  const activeCount = useMemo(
    () => operations.filter((o) => !isFinished(o)).length,
    [operations]
  );

  return { ...result, operations, activeCount };
};

export const useClearFinishedOperations = () => {
  const queryClient = useQueryClient();

  return useApiMutation<unknown, void>({
    path: `${PATH}/finished`,
    method: 'DELETE',
    mutationOptions: {
      onSuccess: () => {
        queryClient.setQueriesData(
          { queryKey: [PATH] },
          (old: FileOperation[] | undefined) =>
            old?.filter((o) => !isFinished(o))
        );
      },
    },
  });
};
