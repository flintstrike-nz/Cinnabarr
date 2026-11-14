import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import DoctorReview from 'Doctor/DoctorReview';
import DoctorSettingsPanel from 'Doctor/DoctorSettingsPanel';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import styles from './Movie.module.css';

interface ReorganiseEntry {
  mediaType: string;
  mediaId: number;
  title?: string;
  fileId: number;
  from: string;
  to: string;
}

interface ReorganiseRun {
  runId: string;
  applied: string;
  files: number;
  undone: number;
}

const NO_ENTRIES: ReorganiseEntry[] = [];
const NO_RUNS: ReorganiseRun[] = [];

function fileName(path: string) {
  return path.split(/[\\/]/).pop();
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

// The library reorganise tool (roadmap §4.12): the files whose names don't follow the naming format, renamed in one go,
// with each run kept so it can be undone.
function Doctor() {
  const queryClient = useQueryClient();
  const [isConfirmOpen, setIsConfirmOpen] = useState(false);
  const {
    data: entries = NO_ENTRIES,
    isLoading,
    error,
  } = useApiQuery<ReorganiseEntry[]>({
    path: '/reorganise/preview',
    queryOptions: { refetchOnWindowFocus: false },
  });
  const { data: runs = NO_RUNS } = useApiQuery<ReorganiseRun[]>({
    path: '/reorganise/runs',
    queryOptions: { refetchInterval: 5000 },
  });

  const {
    mutate: sendCommand,
    isPending,
    error: commandError,
  } = useApiMutation<unknown, Record<string, string>>({
    path: '/command',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        // The command runs in the background; the lists catch up as it goes.
        setTimeout(() => {
          queryClient.invalidateQueries({ queryKey: ['/reorganise/preview'] });
          queryClient.invalidateQueries({ queryKey: ['/reorganise/runs'] });
        }, 4000);
      },
    },
  });

  const handleReorganisePress = useCallback(() => {
    setIsConfirmOpen(true);
  }, []);

  const handleConfirmClose = useCallback(() => {
    setIsConfirmOpen(false);
  }, []);

  const handleConfirm = useCallback(() => {
    setIsConfirmOpen(false);
    sendCommand({ name: 'ReorganiseLibrary' });
  }, [sendCommand]);

  const handleRunPress = useCallback(() => {
    sendCommand({ name: 'Doctor' });
    setTimeout(() => {
      queryClient.invalidateQueries({ queryKey: ['/doctor'] });
    }, 6000);
  }, [sendCommand, queryClient]);

  const handleUndo = useCallback(
    (runId: string) => {
      sendCommand({ name: 'ReorganiseUndo', runId });
    },
    [sendCommand]
  );

  return (
    <PageContent title={translate('Doctor')}>
      <PageContentBody>
        <PageHeading title={translate('Doctor')} />

        <p>{translate('DoctorCaption')}</p>

        <Button isDisabled={isPending} onPress={handleRunPress}>
          {translate('DoctorRunNow')}
        </Button>

        <DoctorReview />

        <DoctorSettingsPanel />

        <h3>{translate('Reorganise')}</h3>
        <p>{translate('ReorganiseCaption')}</p>

        {isLoading ? <LoadingIndicator /> : null}
        {error ? <p>{getErrorMessage(error)}</p> : null}
        {commandError ? <p>{getErrorMessage(commandError)}</p> : null}

        {!isLoading && !error && !entries.length ? (
          <p>{translate('ReorganiseNothing')}</p>
        ) : null}

        {entries.length ? (
          <>
            <Button
              kind={kinds.PRIMARY}
              isDisabled={isPending}
              onPress={handleReorganisePress}
            >
              {translate('ReorganiseFiles', { count: entries.length })}
            </Button>

            <table className={styles.table}>
              <thead>
                <tr>
                  <th>{translate('Title')}</th>
                  <th>{translate('ReorganiseFrom')}</th>
                  <th>{translate('ReorganiseTo')}</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr key={`${entry.mediaType}-${entry.fileId}`}>
                    <td>{entry.title}</td>
                    <td>{fileName(entry.from)}</td>
                    <td>{fileName(entry.to)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        ) : null}

        {runs.length ? (
          <>
            <h2>{translate('ReorganiseRuns')}</h2>

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
                    isBusy={isPending}
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
          message={translate('ReorganiseConfirm', { count: entries.length })}
          confirmLabel={translate('Reorganise')}
          onConfirm={handleConfirm}
          onCancel={handleConfirmClose}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default Doctor;
