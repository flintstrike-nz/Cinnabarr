import React, { useCallback, useState } from 'react';
import {
  EngineQueueMove,
  getEngineErrorMessage,
  useEngineDownloadAction,
  useEngineForceStart,
  useEngineQueueMove,
  useEngineSpeedLimits,
} from 'Activity/Queue/Engine/useEngineControls';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ProgressBar from 'Components/ProgressBar';
import { icons, kinds, sizes } from 'Helpers/Props';
import formatTimeSpan from 'Utilities/Date/formatTimeSpan';
import translate from 'Utilities/String/translate';
import useEngineDownloadFiles, {
  EngineDownloadFile,
  EngineFileState,
  EngineSeedingGoal,
} from './useEngineDownloadFiles';
import styles from './QueueFileDetails.module.css';

const stateLabels: Record<EngineFileState, string> = {
  pending: 'EngineFileStatePending',
  renamed: 'EngineFileStateRenamed',
  imported: 'EngineFileStateImported',
  rejected: 'EngineFileStateRejected',
  replaced: 'EngineFileStateReplaced',
};

const sourceLabels: Record<string, string> = {
  episode: 'EngineSeedingSourceEpisode',
  season: 'EngineSeedingSourceSeason',
  series: 'EngineSeedingSourceSeries',
  global: 'EngineSeedingSourceGlobal',
  tracker: 'EngineSeedingSourceTracker',
};

function formatMinutes(minutes: number) {
  const days = minutes / (24 * 60);

  return Number.isInteger(days)
    ? translate('EngineSeedingDays', { days })
    : translate('EngineSeedingHours', { hours: Math.round(minutes / 60) });
}

function formatTarget(goal: EngineSeedingGoal, ratio: string) {
  switch (goal.mode) {
    case 'none':
      return translate('EngineSeedingModeNone');
    case 'time':
      return formatMinutes(goal.minutes);
    case 'ratio':
      return ratio;
    default:
      return translate(
        goal.mode === 'either'
          ? 'EngineSeedingGoalEither'
          : 'EngineSeedingGoalBoth',
        { time: formatMinutes(goal.minutes), ratio }
      );
  }
}

// A goal as text, e.g. "14 days or ratio 1, from series".
function formatGoal(goal: EngineSeedingGoal) {
  const source = translate(
    sourceLabels[goal.source] ?? 'EngineSeedingSourceGlobal'
  );
  const ratio = translate('EngineSeedingRatioValue', { ratio: goal.ratio });

  const target = formatTarget(goal, ratio);

  return translate('EngineSeedingGoalFrom', { goal: target, source });
}

function FileRow({
  file,
  ratio,
}: {
  file: EngineDownloadFile;
  ratio?: number;
}) {
  const seeded = file.importedAt
    ? Date.now() - new Date(file.importedAt).getTime()
    : null;

  return (
    <tr className={file.isSubtitle ? styles.subtitle : undefined}>
      <td className={styles.path}>{file.path}</td>

      <td>{translate(stateLabels[file.state])}</td>

      <td className={styles.progress}>
        {file.progress == null ? (
          '—'
        ) : (
          <ProgressBar
            progress={file.progress * 100}
            size={sizes.SMALL}
            showText={true}
          />
        )}
      </td>

      <td>
        {file.goals.length
          ? file.goals.map((goal) => (
              <div
                key={`${goal.source}-${goal.mode}-${goal.minutes}-${goal.ratio}`}
                className={goal.isMet ? styles.met : undefined}
              >
                {formatGoal(goal)}
                {goal.isMet ? ` (${translate('EngineSeedingGoalMet')})` : null}
              </div>
            ))
          : '—'}
      </td>

      <td>
        {file.goals.length && seeded != null
          ? translate('EngineSeedingProgress', {
              time: formatTimeSpan(seeded),
              ratio: ratio == null ? '—' : ratio.toFixed(2),
            })
          : null}
      </td>
    </tr>
  );
}

const QUEUE_MOVES: { move: EngineQueueMove; label: string }[] = [
  { move: 'top', label: 'EngineMoveTop' },
  { move: 'up', label: 'EngineMoveUp' },
  { move: 'down', label: 'EngineMoveDown' },
  { move: 'bottom', label: 'EngineMoveBottom' },
];

interface QueueFileDetailsProps {
  downloadId: string;
}

// The files of a built-in engine download: library path, state, progress and seeding goals (roadmap §6.2).
function QueueFileDetails({ downloadId }: QueueFileDetailsProps) {
  const { data, error, isFetched } = useEngineDownloadFiles(downloadId);
  const pause = useEngineDownloadAction(downloadId, 'pause');
  const resume = useEngineDownloadAction(downloadId, 'resume');
  const recheck = useEngineDownloadAction(downloadId, 'recheck');
  const forceStart = useEngineForceStart(downloadId);
  const queueMove = useEngineQueueMove(downloadId);
  const speedLimits = useEngineSpeedLimits(downloadId);
  const [downloadLimit, setDownloadLimit] = useState<number | null>(null);
  const [uploadLimit, setUploadLimit] = useState<number | null>(null);

  const isPaused = !!data?.isPaused;
  const isForceStarted = !!data?.isForceStarted;
  const actionError =
    getEngineErrorMessage(pause.error) ??
    getEngineErrorMessage(resume.error) ??
    getEngineErrorMessage(recheck.error) ??
    getEngineErrorMessage(forceStart.error) ??
    getEngineErrorMessage(queueMove.error) ??
    getEngineErrorMessage(speedLimits.error);

  const handlePauseOrResumePress = useCallback(() => {
    if (isPaused) {
      resume.mutate();
    } else {
      pause.mutate();
    }
  }, [isPaused, pause, resume]);

  const handleForceStartPress = useCallback(() => {
    forceStart.mutate(!isForceStarted);
  }, [isForceStarted, forceStart]);

  const handleMovePress = useCallback(
    (event: React.MouseEvent<HTMLElement>) => {
      const move = event.currentTarget.dataset.move as EngineQueueMove;

      queueMove.mutate(move);
    },
    [queueMove]
  );

  const handleDownloadLimitChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setDownloadLimit(Number(event.target.value));
    },
    []
  );

  const handleUploadLimitChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setUploadLimit(Number(event.target.value));
    },
    []
  );

  const handleApplyLimitsPress = useCallback(() => {
    speedLimits.mutate({
      download: downloadLimit ?? data?.downloadLimit ?? 0,
      upload: uploadLimit ?? data?.uploadLimit ?? 0,
    });
  }, [speedLimits, downloadLimit, uploadLimit, data]);

  const handleRecheckPress = useCallback(() => {
    recheck.mutate();
  }, [recheck]);

  if (!isFetched) {
    return <LoadingIndicator size={20} />;
  }

  if (error || !data) {
    return (
      <Alert kind={kinds.INFO}>{translate('EngineFilesUnavailable')}</Alert>
    );
  }

  // A download still being prepared starts by itself once its files are chosen (roadmap §4.8).
  const canStart = data.inEngine && !data.isAwaitingPreparation;

  return (
    <div>
      {data.inEngine ? (
        <div className={styles.actions}>
          <SpinnerButton
            isSpinning={pause.isPending || resume.isPending}
            isDisabled={isPaused && !canStart}
            onPress={handlePauseOrResumePress}
          >
            <Icon name={isPaused ? icons.ENGINE_RESUME : icons.ENGINE_PAUSE} />{' '}
            {translate(isPaused ? 'EngineResume' : 'EnginePause')}
          </SpinnerButton>

          <SpinnerButton
            isSpinning={forceStart.isPending}
            isDisabled={!isForceStarted && !canStart}
            onPress={handleForceStartPress}
          >
            <Icon name={icons.ENGINE_FORCE_START} />{' '}
            {translate(
              isForceStarted ? 'EngineStopForceStart' : 'EngineForceStart'
            )}
          </SpinnerButton>

          <SpinnerButton
            isSpinning={recheck.isPending}
            onPress={handleRecheckPress}
          >
            <Icon name={icons.REFRESH} /> {translate('EngineRecheck')}
          </SpinnerButton>

          {QUEUE_MOVES.map(({ move, label }) => (
            <SpinnerButton
              key={move}
              data-move={move}
              isSpinning={queueMove.isPending}
              onPress={handleMovePress}
            >
              {translate(label)}
            </SpinnerButton>
          ))}

          <label>
            {translate('EngineDownloadLimitRow')}{' '}
            <input
              type="number"
              min={0}
              name="downloadLimit"
              value={downloadLimit ?? data.downloadLimit}
              onChange={handleDownloadLimitChange}
            />
          </label>

          <label>
            {translate('EngineUploadLimitRow')}{' '}
            <input
              type="number"
              min={0}
              name="uploadLimit"
              value={uploadLimit ?? data.uploadLimit}
              onChange={handleUploadLimitChange}
            />
          </label>

          <SpinnerButton
            isSpinning={speedLimits.isPending}
            onPress={handleApplyLimitsPress}
          >
            {translate('EngineApplyLimits')}
          </SpinnerButton>

          {data.isAwaitingPreparation ? (
            <span className={styles.note}>
              {translate('EngineAwaitingPreparation')}
            </span>
          ) : null}
        </div>
      ) : null}

      {actionError ? <Alert kind={kinds.DANGER}>{actionError}</Alert> : null}

      <table className={styles.files}>
        <thead>
          <tr>
            <th>{translate('Path')}</th>
            <th>{translate('Status')}</th>
            <th>{translate('Progress')}</th>
            <th>{translate('EngineSeedingGoal')}</th>
            <th>{translate('EngineSeeded')}</th>
          </tr>
        </thead>

        <tbody>
          {data.files.map((file) => (
            <FileRow key={file.index} file={file} ratio={data.ratio} />
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default QueueFileDetails;
