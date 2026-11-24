import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useMemo, useState } from 'react';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import CheckInput from 'Components/Form/CheckInput';
import PathInput from 'Components/Form/PathInput';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import useRootFolders from 'RootFolder/useRootFolders';
import { CheckInputChanged, InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import styles from './ReorganisePanel.module.css';

type ReorganiseKind = 'rename' | 'folder' | 'import';

interface ReorganiseEntry {
  kind: ReorganiseKind;
  mediaType: string;
  mediaId: number;
  title?: string;
  fileId: number;
  from: string;
  to?: string;

  // Why the entry won't be done; missing when it will be.
  problem?: string;
}

interface ReorganiseRun {
  runId: string;
  applied: string;
  files: number;
  undone: number;
}

const NO_ENTRIES: ReorganiseEntry[] = [];
const NO_RUNS: ReorganiseRun[] = [];

// The scope choices: every root folder, one root folder, or a folder typed or browsed to.
const ALL_ROOTS = '';
const A_FOLDER = '*folder';

function fileName(path?: string) {
  return path?.split(/[\\/]/).pop() ?? '';
}

function kindLabel(kind: ReorganiseKind) {
  switch (kind) {
    case 'folder':
      return translate('ReorganiseKindFolder');
    case 'import':
      return translate('ReorganiseKindImport');
    default:
      return translate('ReorganiseKindRename');
  }
}

function entryKey(entry: ReorganiseEntry) {
  return `${entry.kind}-${entry.mediaType}-${entry.fileId}-${entry.from}`;
}

interface EntryRowProps {
  entry: ReorganiseEntry;
}

function EntryRow({ entry }: EntryRowProps) {
  return (
    <tr className={entry.problem ? styles.blocked : undefined}>
      <td>{entry.title ?? '—'}</td>
      <td>{kindLabel(entry.kind)}</td>
      <td className={styles.path} title={entry.from}>
        {fileName(entry.from)}
      </td>
      <td className={styles.path} title={entry.to}>
        {entry.problem ? entry.problem : fileName(entry.to)}
      </td>
    </tr>
  );
}

interface RunRowProps {
  run: ReorganiseRun;
  isBusy: boolean;
  onUndo: (runId: string) => void;
}

function RunRow({ run, isBusy, onUndo }: RunRowProps) {
  const handleUndoPress = useCallback(() => {
    onUndo(run.runId);
  }, [run.runId, onUndo]);

  return (
    <tr>
      <td>{new Date(run.applied).toLocaleString()}</td>
      <td>{run.files}</td>
      <td>{run.undone}</td>
      <td>
        <Button
          isDisabled={isBusy || run.undone >= run.files}
          onPress={handleUndoPress}
        >
          {translate('ReorganiseUndo')}
        </Button>
      </td>
    </tr>
  );
}

// The library reorganise tool (roadmap §4.12): in every root folder, one root folder or one folder, the files whose
// names don't follow the naming format, the loose files that can go into a series or movie, and (when asked) the
// series and movie folders to rename. Whatever can't be done says why. Each run is kept so its renames can be undone.
function ReorganisePanel() {
  const queryClient = useQueryClient();
  const executeCommand = useExecuteCommand();
  const { data: rootFolders } = useRootFolders();
  const [scope, setScope] = useState(ALL_ROOTS);
  const [typedFolder, setTypedFolder] = useState('');
  const [chosenFolder, setChosenFolder] = useState('');
  const [includeFolders, setIncludeFolders] = useState(false);
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);

  const libraryRoots = useMemo(
    () => rootFolders.filter((r) => r.library !== 'games'),
    [rootFolders]
  );

  const folder = scope === A_FOLDER ? chosenFolder : scope;
  const isFolderPending = scope === A_FOLDER && !chosenFolder;

  const {
    data: entries = NO_ENTRIES,
    isFetching,
    error,
    refetch,
  } = useApiQuery<ReorganiseEntry[]>({
    path: '/reorganise/preview',
    queryParams: { folder, includeFolders },
    queryOptions: { enabled: !isFolderPending, refetchOnWindowFocus: false },
  });

  const { data: runs = NO_RUNS } = useApiQuery<ReorganiseRun[]>({
    path: '/reorganise/runs',
  });

  const isReorganising = useCommandExecuting('ReorganiseLibrary');
  const isUndoing = useCommandExecuting('ReorganiseUndo');
  const isBusy = isReorganising || isUndoing;

  const { ready, blocked } = useMemo(() => {
    return {
      ready: entries.filter((e) => !e.problem),
      blocked: entries.filter((e) => !!e.problem),
    };
  }, [entries]);

  const refresh = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ['/reorganise/preview'] });
    queryClient.invalidateQueries({ queryKey: ['/reorganise/runs'] });
  }, [queryClient]);

  const handleScopeChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setScope(event.target.value);
    },
    []
  );

  const handleTypedFolderChange = useCallback(
    ({ value }: InputChanged<string>) => {
      setTypedFolder(value);
    },
    []
  );

  const handleShowFolderPress = useCallback(() => {
    if (typedFolder === chosenFolder) {
      refetch();
    } else {
      setChosenFolder(typedFolder);
    }
  }, [typedFolder, chosenFolder, refetch]);

  const handleIncludeFoldersChange = useCallback(
    ({ value }: CheckInputChanged) => {
      setIncludeFolders(value);
    },
    []
  );

  const handleReorganisePress = useCallback(() => {
    setIsConfirmOpen(true);
  }, []);

  const handleConfirmClose = useCallback(() => {
    setIsConfirmOpen(false);
  }, []);

  const handleConfirm = useCallback(() => {
    setIsConfirmOpen(false);
    executeCommand(
      { name: 'ReorganiseLibrary', folder, includeFolders },
      refresh
    );
  }, [executeCommand, folder, includeFolders, refresh]);

  const handleUndo = useCallback(
    (runId: string) => {
      executeCommand({ name: 'ReorganiseUndo', runId }, refresh);
    },
    [executeCommand, refresh]
  );

  const hasImports = ready.some((e) => e.kind === 'import');

  return (
    <>
      <h3>{translate('Reorganise')}</h3>
      <p>{translate('ReorganiseCaption')}</p>

      <div className={styles.scope}>
        <select
          className={styles.select}
          aria-label={translate('ReorganiseScope')}
          value={scope}
          onChange={handleScopeChange}
        >
          <option value={ALL_ROOTS}>{translate('ReorganiseAllRoots')}</option>
          {libraryRoots.map((rootFolder) => (
            <option key={rootFolder.id} value={rootFolder.path}>
              {rootFolder.accessible
                ? rootFolder.path
                : translate('ReorganiseRootUnreachable', {
                    path: rootFolder.path,
                  })}
            </option>
          ))}
          <option value={A_FOLDER}>{translate('ReorganiseAFolder')}</option>
        </select>

        <div className={styles.option}>
          <CheckInput
            name="includeFolders"
            ariaLabel={translate('ReorganiseIncludeFolders')}
            value={includeFolders}
            onChange={handleIncludeFoldersChange}
          />
          {translate('ReorganiseIncludeFolders')}
        </div>
      </div>

      {scope === A_FOLDER ? (
        <div className={styles.folder}>
          <PathInput
            className={styles.pathInput}
            name="folder"
            value={typedFolder}
            includeFiles={false}
            onChange={handleTypedFolderChange}
          />

          <Button isDisabled={!typedFolder} onPress={handleShowFolderPress}>
            {translate('ReorganiseShow')}
          </Button>
        </div>
      ) : null}

      {isFetching ? <LoadingIndicator /> : null}
      {error ? <p>{getErrorMessage(error)}</p> : null}

      {!isFetching && !error && !isFolderPending && !entries.length ? (
        <p>{translate('ReorganiseNothing')}</p>
      ) : null}

      {!isFetching && entries.length ? (
        <>
          <div className={styles.summary}>
            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={isReorganising}
              isDisabled={isBusy || !ready.length}
              onPress={handleReorganisePress}
            >
              {translate('ReorganiseFiles', { count: ready.length })}
            </SpinnerButton>

            {blocked.length ? (
              <span className={styles.blockedCount}>
                {translate('ReorganiseBlockedCount', { count: blocked.length })}
              </span>
            ) : null}
          </div>

          {ready.length ? (
            <table className={styles.table}>
              <thead>
                <tr>
                  <th>{translate('Title')}</th>
                  <th>{translate('ReorganiseWhat')}</th>
                  <th>{translate('ReorganiseFrom')}</th>
                  <th>{translate('ReorganiseTo')}</th>
                </tr>
              </thead>
              <tbody>
                {ready.map((entry) => (
                  <EntryRow key={entryKey(entry)} entry={entry} />
                ))}
              </tbody>
            </table>
          ) : null}

          {blocked.length ? (
            <>
              <h4>{translate('ReorganiseBlocked')}</h4>

              <table className={styles.table}>
                <thead>
                  <tr>
                    <th>{translate('Title')}</th>
                    <th>{translate('ReorganiseWhat')}</th>
                    <th>{translate('ReorganiseFrom')}</th>
                    <th>{translate('ReorganiseWhyNot')}</th>
                  </tr>
                </thead>
                <tbody>
                  {blocked.map((entry) => (
                    <EntryRow key={entryKey(entry)} entry={entry} />
                  ))}
                </tbody>
              </table>
            </>
          ) : null}
        </>
      ) : null}

      {runs.length ? (
        <>
          <h4>{translate('ReorganiseRuns')}</h4>

          <table className={styles.table}>
            <thead>
              <tr>
                <th>{translate('Date')}</th>
                <th>{translate('ReorganiseRenamed')}</th>
                <th>{translate('ReorganiseUndone')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {runs.map((run) => (
                <RunRow
                  key={run.runId}
                  run={run}
                  isBusy={isBusy}
                  onUndo={handleUndo}
                />
              ))}
            </tbody>
          </table>
        </>
      ) : null}

      <ConfirmModal
        isOpen={isConfirmOpen}
        kind={kinds.DANGER}
        title={translate('Reorganise')}
        message={
          hasImports
            ? translate('ReorganiseConfirmWithImports', { count: ready.length })
            : translate('ReorganiseConfirm', { count: ready.length })
        }
        confirmLabel={translate('Reorganise')}
        onConfirm={handleConfirm}
        onCancel={handleConfirmClose}
      />
    </>
  );
}

export default ReorganisePanel;
