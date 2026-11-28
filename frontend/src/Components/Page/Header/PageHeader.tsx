import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toggleIsSidebarVisible, useAppDimension } from 'App/appStore';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import useKeyboardShortcuts from 'Helpers/Hooks/useKeyboardShortcuts';
import { icons } from 'Helpers/Props';
import { toggleOperationsPanel } from 'Operations/operationsPanelStore';
import { useFileOperations } from 'Operations/useFileOperations';
import translate from 'Utilities/String/translate';
import KeyboardShortcutsModal from './KeyboardShortcutsModal';
import PageHeaderActionsMenu from './PageHeaderActionsMenu';
import PageHeaderToolsMenu from './PageHeaderToolsMenu';
import SeriesSearchInput from './SeriesSearchInput';
import styles from './PageHeader.module.css';

function PageHeader() {
  const [isKeyboardShortcutsModalOpen, setIsKeyboardShortcutsModalOpen] =
    useState(false);

  const { bindShortcut, unbindShortcut } = useKeyboardShortcuts();
  const isSmallScreen = useAppDimension('isSmallScreen');
  const navigate = useNavigate();
  const { activeCount } = useFileOperations();

  const handleSidebarToggle = useCallback(() => {
    toggleIsSidebarVisible();
  }, []);

  // The operations queue: the panel on a wide screen, its own page on a phone.
  const handleOperationsPress = useCallback(() => {
    if (isSmallScreen) {
      navigate('/operations');
    } else {
      toggleOperationsPanel();
    }
  }, [isSmallScreen, navigate]);

  const handleOpenKeyboardShortcutsModal = useCallback(() => {
    setIsKeyboardShortcutsModalOpen(true);
  }, []);

  const handleKeyboardShortcutsModalClose = useCallback(() => {
    setIsKeyboardShortcutsModalOpen(false);
  }, []);

  useEffect(() => {
    bindShortcut(
      'openKeyboardShortcutsModal',
      handleOpenKeyboardShortcutsModal
    );

    return () => {
      unbindShortcut('openKeyboardShortcutsModal');
    };
  }, [handleOpenKeyboardShortcutsModal, bindShortcut, unbindShortcut]);

  return (
    <div className={styles.header}>
      <div className={styles.logoContainer}>
        <Link
          className={styles.logoLink}
          to="/library"
          aria-label={translate('Home')}
        >
          <img
            className={styles.logo}
            src={`${window.Sonarr.urlBase}/Content/Images/cinnabarr-mark.svg`}
            alt=""
          />
          <span className={styles.brandName}>{window.Sonarr.instanceName}</span>
        </Link>
      </div>

      <div className={styles.sidebarToggleContainer}>
        <IconButton
          id="sidebar-toggle-button"
          className={styles.sidebarToggle}
          name={icons.NAVBAR_COLLAPSE}
          aria-label={translate('Menu')}
          onPress={handleSidebarToggle}
        />
      </div>

      <SeriesSearchInput />

      <div className={styles.right}>
        <IconButton
          className={styles.operations}
          iconClassName={styles.operationsIcon}
          name={icons.OPERATIONS}
          aria-label={translate('Operations')}
          title={translate('Operations')}
          onPress={handleOperationsPress}
        />

        {activeCount ? (
          <span className={styles.operationsBadge}>{activeCount}</span>
        ) : null}

        <PageHeaderToolsMenu />

        <PageHeaderActionsMenu
          onKeyboardShortcutsPress={handleOpenKeyboardShortcutsModal}
        />
      </div>

      <KeyboardShortcutsModal
        isOpen={isKeyboardShortcutsModalOpen}
        onModalClose={handleKeyboardShortcutsModalClose}
      />
    </div>
  );
}

export default PageHeader;
