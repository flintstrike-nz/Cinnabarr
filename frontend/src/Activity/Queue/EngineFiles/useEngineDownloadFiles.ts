import useApiQuery from 'Helpers/Hooks/useApiQuery';

export type EngineFileState =
  | 'pending'
  | 'renamed'
  | 'imported'
  | 'rejected'
  | 'replaced';

export interface EngineSeedingGoal {
  mode: 'none' | 'time' | 'ratio' | 'either' | 'both';
  minutes: number;
  ratio: number;
  source: string;
  isMet: boolean;
}

export interface EngineDownloadFile {
  index: number;
  path: string;
  isSubtitle: boolean;
  state: EngineFileState;
  progress?: number;
  importedAt?: string;
  goals: EngineSeedingGoal[];
}

export interface EngineDownloadFiles {
  inEngine: boolean;
  isPaused: boolean;
  isForceStarted: boolean;
  isAwaitingPreparation: boolean;
  ratio?: number;
  files: EngineDownloadFile[];
}

// Each file of a built-in engine download (roadmap §6.2), refreshed while shown.
const useEngineDownloadFiles = (downloadId: string) => {
  return useApiQuery<EngineDownloadFiles>({
    path: `/engine/downloads/${downloadId}/files`,
    queryOptions: { refetchInterval: 10000 },
  });
};

export default useEngineDownloadFiles;
