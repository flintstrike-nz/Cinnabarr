import classNames from 'classnames';
import React from 'react';
import ProgressBar from 'Components/ProgressBar';
import { kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import { FileOperation, FileOperationStatus } from './useFileOperations';
import styles from './OperationsList.module.css';

const KIND_LABELS: Record<FileOperation['kind'], string> = {
  import: 'OperationKindImport',
  move: 'OperationKindMove',
  copy: 'OperationKindCopy',
  rename: 'OperationKindRename',
};

const STATUS_LABELS: Record<FileOperationStatus, string> = {
  queued: 'OperationStatusQueued',
  running: 'OperationStatusRunning',
  completed: 'OperationStatusCompleted',
  skipped: 'OperationStatusSkipped',
  failed: 'OperationStatusFailed',
};

const PROGRESS_KINDS = {
  queued: kinds.INFO,
  running: kinds.PRIMARY,
  completed: kinds.SUCCESS,
  skipped: kinds.WARNING,
  failed: kinds.DANGER,
} as const;

const fileName = (path?: string) => path?.split(/[\\/]/).pop() ?? '';

interface OperationsListProps {
  operations: FileOperation[];
}

// One row per file per step, in the order they run.
function OperationsList({ operations }: OperationsListProps) {
  if (!operations.length) {
    return <p className={styles.empty}>{translate('OperationsEmpty')}</p>;
  }

  return (
    <ol className={styles.list}>
      {operations.map((operation) => {
        const target =
          operation.kind === 'import' ? undefined : operation.destination;

        return (
          <li
            key={operation.id}
            className={classNames(
              styles.row,
              operation.status === 'failed' && styles.failed
            )}
            title={operation.destination ?? operation.path}
          >
            <div className={styles.top}>
              <span className={styles.kind}>
                {translate(KIND_LABELS[operation.kind])}
              </span>
              <span className={styles.title}>
                {operation.title ?? fileName(operation.path)}
              </span>
              <span className={styles.status}>
                {operation.status === 'running' && operation.size
                  ? `${operation.progress.toFixed(0)}% of ${formatBytes(
                      operation.size
                    )}`
                  : translate(STATUS_LABELS[operation.status])}
              </span>
            </div>

            <div className={styles.file}>
              {fileName(operation.path)}
              {target && fileName(target) !== fileName(operation.path)
                ? ` → ${fileName(target)}`
                : null}
            </div>

            <ProgressBar
              progress={operation.progress}
              precision={0}
              kind={PROGRESS_KINDS[operation.status]}
              size="small"
            />

            {operation.message ? (
              <div className={styles.message}>{operation.message}</div>
            ) : null}
          </li>
        );
      })}
    </ol>
  );
}

export default OperationsList;
