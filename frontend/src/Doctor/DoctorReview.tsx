import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  DoctorFile,
  DoctorFlag,
  getScreenshotUrl,
  useDeleteFlag,
  useDoctorFlags,
  useRecoverFlag,
} from './useDoctor';
import styles from './DoctorReview.module.css';

interface CopyProps {
  mediaType: string;
  file?: DoctorFile;
  label: string;
  className: string;
}

function Copy({ mediaType, file, label, className }: CopyProps) {
  if (!file) {
    return null;
  }

  const facts = [
    file.quality,
    file.resolution ? `${file.resolution}p` : null,
    file.videoCodec,
    file.videoBitrate
      ? `${(file.videoBitrate / 1_000_000).toFixed(1)} Mbps`
      : null,
    formatBytes(file.size),
  ].filter(Boolean);

  return (
    <div className={`${styles.copy} ${className}`}>
      <div className={styles.copyTitle}>{label}</div>
      <div className={styles.path}>{file.path}</div>
      <div className={styles.facts}>{facts.join(' · ')}</div>
      <div className={styles.shots}>
        {[0, 1, 2].map((index) => (
          <img
            key={index}
            className={styles.shot}
            loading="lazy"
            alt=""
            src={getScreenshotUrl(mediaType, file.id, index)}
          />
        ))}
      </div>
    </div>
  );
}

function ReviewItem({ flag }: { flag: DoctorFlag }) {
  const [isDeleteOpen, setIsDeleteOpen] = useState(false);
  const { deleteFlag, isDeleting, error: deleteError } = useDeleteFlag(flag.id);
  const {
    recover,
    isRecovering,
    error: recoverError,
  } = useRecoverFlag(flag.id);

  const handleDeletePress = useCallback(() => setIsDeleteOpen(true), []);
  const handleDeleteClose = useCallback(() => setIsDeleteOpen(false), []);
  const handleDeleteConfirm = useCallback(() => {
    setIsDeleteOpen(false);
    deleteFlag();
  }, [deleteFlag]);
  const handleKeepThisPress = useCallback(() => recover(false), [recover]);
  const handleKeepBothPress = useCallback(() => recover(true), [recover]);

  const isBusy = isDeleting || isRecovering;
  const error = deleteError ?? recoverError;

  return (
    <div className={styles.item}>
      <div className={styles.header}>
        <span className={styles.title}>{flag.title}</span>
        <span className={styles.reason}>{flag.reason}</span>
        <span>
          {translate('DoctorDeletesOn', {
            date: new Date(flag.deleteAfter).toLocaleDateString(),
          })}
        </span>
      </div>

      <div className={styles.copies}>
        <Copy
          mediaType={flag.mediaType}
          file={flag.flagged}
          label={translate('DoctorFlaggedCopy')}
          className={styles.flagged}
        />
        <Copy
          mediaType={flag.mediaType}
          file={flag.kept}
          label={translate('DoctorKeptCopy')}
          className={styles.kept}
        />
      </div>

      <div className={styles.actions}>
        <Button
          kind={kinds.DANGER}
          isDisabled={isBusy}
          onPress={handleDeletePress}
        >
          {translate('DoctorDeleteFlagged')}
        </Button>
        <Button isDisabled={isBusy} onPress={handleKeepThisPress}>
          {translate('DoctorKeepFlagged')}
        </Button>
        <Button isDisabled={isBusy} onPress={handleKeepBothPress}>
          {translate('DoctorKeepBoth')}
        </Button>
      </div>

      {error ? <div>{getErrorMessage(error)}</div> : null}

      <ConfirmModal
        isOpen={isDeleteOpen}
        kind={kinds.DANGER}
        title={translate('DoctorDeleteFlagged')}
        message={translate('DoctorDeleteConfirm', {
          path: flag.flagged?.path ?? '',
        })}
        confirmLabel={translate('Delete')}
        onConfirm={handleDeleteConfirm}
        onCancel={handleDeleteClose}
      />
    </div>
  );
}

// The duplicates Doctor flagged: both copies side by side with screenshots, the flagged one highlighted.
function DoctorReview() {
  const { data, isLoading } = useDoctorFlags();

  if (isLoading) {
    return null;
  }

  return (
    <div>
      <h3>{translate('DoctorDuplicates')}</h3>

      {data.length ? (
        data.map((flag) => <ReviewItem key={flag.id} flag={flag} />)
      ) : (
        <p>{translate('DoctorNoDuplicates')}</p>
      )}
    </div>
  );
}

export default DoctorReview;
