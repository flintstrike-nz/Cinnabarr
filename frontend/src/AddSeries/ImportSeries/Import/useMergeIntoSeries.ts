import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { create } from 'zustand';
import Command from 'Commands/Command';
import { useExecuteCommand } from 'Commands/useCommands';
import useSeries from 'Series/useSeries';

export interface MergeFolder {
  tvdbId: number;
  path: string;
}

export interface MergeResult {
  path: string;
  merged: boolean;
  reason?: string;
}

export interface MergeReport {
  seriesTitle: string;
  results: MergeResult[];
}

// The reports of the merges finished since the page was opened, shown until they are dismissed.
const mergeReportStore = create<{ reports: MergeReport[] }>(() => ({
  reports: [],
}));

export const useMergeReports = () => mergeReportStore((state) => state.reports);

export const clearMergeReports = () => {
  mergeReportStore.setState({ reports: [] });
};

// Merges folders that belong to a series already in the library into that series: the files are matched to its episodes
// and moved into its folder (the MergeIntoSeries command). When it finishes, what happened to each file is reported.
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
        const seriesTitle =
          existingSeries.find((s) => s.id === seriesId)?.title ?? '';

        setPendingCount((count) => count + 1);

        executeCommand(
          { name: 'MergeIntoSeries', seriesId, paths },
          (command: Command) => {
            setPendingCount((count) => Math.max(count - 1, 0));

            const { results } = command.body as unknown as {
              results?: MergeResult[];
            };

            if (results?.length) {
              mergeReportStore.setState((state) => ({
                reports: [...state.reports, { seriesTitle, results }],
              }));
            }

            // The folders that were merged are gone from the root folder's list, and the series has new files.
            queryClient.invalidateQueries({ queryKey: ['/rootFolder'] });
            queryClient.invalidateQueries({ queryKey: ['/series'] });
            queryClient.invalidateQueries({ queryKey: ['/episode'] });
            queryClient.invalidateQueries({ queryKey: ['/episodeFile'] });
          }
        );
      });
    },
    [existingSeries, executeCommand, queryClient]
  );

  return { mergeFolders, isMerging: pendingCount > 0 };
};
