import React, { useCallback, useState } from 'react';
import { OverflowDivider } from 'Components/Page/Toolbar/Overflow';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import ToolbarItem from 'Components/Page/Toolbar/ToolbarItem';
import { icons } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import AddTorrentModal from './AddTorrentModal';
import {
  useEngineAll,
  useEngineStatus,
  useSetAlternativeSpeedLimits,
} from './useEngineControls';
import styles from './EngineToolbarItems.module.css';

// The built-in engine's header controls on the Downloads page (roadmap §6.2): live speeds, the alternative speed
// limits and pause or resume all. Shown only while the engine answers.
function EngineToolbarItems() {
  const { data: status } = useEngineStatus();
  const { mutate: setAltSpeed, isPending: isSettingAltSpeed } =
    useSetAlternativeSpeedLimits();
  const { mutate: pauseAll, isPending: isPausing } = useEngineAll('pause');
  const { mutate: resumeAll, isPending: isResuming } = useEngineAll('resume');
  const [isAddTorrentOpen, setIsAddTorrentOpen] = useState(false);

  const handleAddTorrentPress = useCallback(() => {
    setIsAddTorrentOpen(true);
  }, []);

  const handleAddTorrentClose = useCallback(() => {
    setIsAddTorrentOpen(false);
  }, []);

  const handleAltSpeedPress = useCallback(() => {
    if (status) {
      setAltSpeed(!status.alternativeSpeedLimits);
    }
  }, [status, setAltSpeed]);

  const handlePauseAllPress = useCallback(() => {
    pauseAll();
  }, [pauseAll]);

  const handleResumeAllPress = useCallback(() => {
    resumeAll();
  }, [resumeAll]);

  if (!status) {
    return null;
  }

  return (
    <>
      <OverflowDivider groupId="engine">
        <PageToolbarSeparator />
      </OverflowDivider>

      <ToolbarItem id="engine-speeds" pinned={true}>
        <span className={styles.speeds} title={translate('EngineSpeeds')}>
          ↓ {formatBytes(status.downloadSpeed)}/s ↑{' '}
          {formatBytes(status.uploadSpeed)}/s
        </span>
      </ToolbarItem>

      <ToolbarItem
        id="engine-add-torrent"
        priority={1}
        groupId="engine"
        label={translate('AddTorrent')}
        iconName={icons.ADD}
        onPress={handleAddTorrentPress}
      />

      <ToolbarItem
        id="engine-alt-speed"
        priority={2}
        groupId="engine"
        label={translate(
          status.alternativeSpeedLimits
            ? 'EngineAltSpeedOn'
            : 'EngineAltSpeedOff'
        )}
        iconName={icons.ENGINE_ALT_SPEED}
        isSpinning={isSettingAltSpeed}
        onPress={handleAltSpeedPress}
      />

      <ToolbarItem
        id="engine-pause-all"
        priority={2}
        groupId="engine"
        label={translate('EnginePauseAll')}
        iconName={icons.ENGINE_PAUSE}
        isSpinning={isPausing}
        onPress={handlePauseAllPress}
      />

      <ToolbarItem
        id="engine-resume-all"
        priority={2}
        groupId="engine"
        label={translate('EngineResumeAll')}
        iconName={icons.ENGINE_RESUME}
        isSpinning={isResuming}
        onPress={handleResumeAllPress}
      />

      <AddTorrentModal
        isOpen={isAddTorrentOpen}
        onModalClose={handleAddTorrentClose}
      />
    </>
  );
}

export default EngineToolbarItems;
