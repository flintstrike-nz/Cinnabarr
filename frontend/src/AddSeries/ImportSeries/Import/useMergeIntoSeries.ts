import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { useExecuteCommand } from 'Commands/useCommands';
import useSeries from 'Series/useSeries';

export interface MergeFolder {
  tvdbId: number;
  path: string;
}

// Merges folders that belong to a series already in the library into that series: the files are matched to its episodes
// and moved into its folder (the MergeIntoSeries command).
export const useMergeIntoSeries = () => {
  const queryClient = useQueryClient();
  const executeCommand = useExecuteCommand();
  const { data: existingSeries = [] } = useSeries();
  const [pendingCount, setPendingCount] = useState(0);

  const mergeFolders = useCallback(
    (folders: MergeFolder[]) => {
      const pathsBySeriesId = new Map<number, string[]>();

      folders.forEach(({ tvdbId, path }) => {
        const series = existingSeries.find((s) => s.tvdbId === tvdbId);

        if (series) {
          pathsBySeriesId.set(series.id, [
            ...(pathsBySeriesId.get(series.id) ?? []),
            path,
          ]);
        }
      });

      pathsBySeriesId.forEach((paths, seriesId) => {
        setPendingCount((count) => count + 1);

        executeCommand({ name: 'MergeIntoSeries', seriesId, paths }, () => {
          setPendingCount((count) => Math.max(count - 1, 0));

          // The folders that were merged are gone from the root folder's list, and the series has new files.
          queryClient.invalidateQueries({ queryKey: ['/rootFolder'] });
          queryClient.invalidateQueries({ queryKey: ['/series'] });
          queryClient.invalidateQueries({ queryKey: ['/episode'] });
          queryClient.invalidateQueries({ queryKey: ['/episodeFile'] });
        });
      });
    },
    [existingSeries, executeCommand, queryClient]
  );

  return { mergeFolders, isMerging: pendingCount > 0 };
};
