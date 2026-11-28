import React, { useCallback } from 'react';
import { useLocation } from 'react-router-dom';
import { useAppDimension } from 'App/appStore';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import Scroller from 'Components/Scroller/Scroller';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import OperationsList from './OperationsList';
import {
  setIsOperationsPanelOpen,
  useIsOperationsPanelOpen,
} from './operationsPanelStore';
import {
  useClearFinishedOperations,
  useFileOperations,
} from './useFileOperations';
import styles from './OperationsPanel.module.css';

// The operations queue floating on the right of every page, or minimised to a handle on the edge. On a phone
// it is the full-screen Operations page instead.
function OperationsPanel() {
  const isSmallScreen = useAppDimension('isSmallScreen');
  const isOpen = useIsOperationsPanelOpen();
  const { pathname } = useLocation();
  const { operations, activeCount } = useFileOperations();
  const { mutate: clearFinished, isPending: isClearing } =
    useClearFinishedOperations();

  const handleClose = useCallback(() => setIsOperationsPanelOpen(false), []);
  const handleOpen = useCallback(() => setIsOperationsPanelOpen(true), []);
  const handleClear = useCallback(() => clearFinished(), [clearFinished]);

  if (isSmallScreen || pathname === '/operations') {
    return null;
  }

  if (!isOpen) {
    return (
      <Link
        className={styles.handle}
        aria-label={translate('OperationsShow')}
        title={translate('OperationsShow')}
        onPress={handleOpen}
      >
        <Icon name={icons.OPERATIONS} size={16} />
        {activeCount ? (
          <span className={styles.badge}>{activeCount}</span>
        ) : null}
      </Link>
    );
  }

  const hasFinished = operations.length > activeCount;

  return (
    <aside className={styles.panel} aria-label={translate('Operations')}>
      <div className={styles.header}>
        <span className={styles.heading}>
          {translate('Operations')}
          {activeCount ? (
            <span className={styles.count}>
              {translate('OperationsActive', { count: activeCount })}
            </span>
          ) : null}
        </span>

        {hasFinished ? (
          <Link
            className={styles.clear}
            isDisabled={isClearing}
            onPress={handleClear}
          >
            {translate('OperationsClearFinished')}
          </Link>
        ) : null}

        <IconButton
          name={icons.MINIMISE}
          aria-label={translate('OperationsMinimise')}
          title={translate('OperationsMinimise')}
          onPress={handleClose}
        />
      </div>

      <Scroller className={styles.body} scrollDirection="vertical">
        <OperationsList operations={operations} />
      </Scroller>
    </aside>
  );
}

export default OperationsPanel;
