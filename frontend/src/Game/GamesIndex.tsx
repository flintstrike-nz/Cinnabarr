import React, { useCallback, useMemo, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useCommandExecuting, useExecuteCommand } from 'Commands/useCommands';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { icons, kinds } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import GameMetadataSettings from './GameMetadataSettings';
import { getCoverUrl, useGamePlatforms, useGames } from './useGames';
import styles from './GamesIndex.module.css';

interface PlatformButtonProps {
  value: string;
  label: string;
  selected: boolean;
  onSelect: (value: string) => void;
}

function PlatformButton({
  value,
  label,
  selected,
  onSelect,
}: PlatformButtonProps) {
  const handlePress = useCallback(() => onSelect(value), [onSelect, value]);

  return (
    <Button
      kind={selected ? kinds.PRIMARY : kinds.DEFAULT}
      onPress={handlePress}
    >
      {label}
    </Button>
  );
}

// The games library: every ROM and disc image found in the Games root folders.
function GamesIndex() {
  const { data: games, isLoading, error } = useGames();
  const { data: platforms } = useGamePlatforms();
  const executeCommand = useExecuteCommand();
  const isScanning = useCommandExecuting(CommandNames.ScanGames);
  const [platform, setPlatform] = useState('all');

  const handleScanPress = useCallback(() => {
    executeCommand({ name: CommandNames.ScanGames });
  }, [executeCommand]);

  const visible = useMemo(
    () =>
      games.filter(
        (game) => platform === 'all' || game.platformKey === platform
      ),
    [games, platform]
  );

  const present = useMemo(
    () => platforms.filter((p) => games.some((g) => g.platformKey === p.key)),
    [platforms, games]
  );

  return (
    <PageContent title={translate('Games')}>
      <PageToolbar>
        <PageToolbarButton
          label={translate('ScanGames')}
          iconName={icons.REFRESH}
          isSpinning={isScanning}
          onPress={handleScanPress}
        />
      </PageToolbar>

      <PageContentBody>
        {isLoading ? <LoadingIndicator /> : null}
        {error ? <div>{getErrorMessage(error)}</div> : null}

        {!isLoading && !error && !games.length ? (
          <div>
            <p>{translate('NoGamesFound')}</p>
            <Button to="/settings/mediamanagement" kind={kinds.PRIMARY}>
              {translate('RootFolders')}
            </Button>
          </div>
        ) : null}

        {present.length > 1 ? (
          <div className={styles.filters}>
            <PlatformButton
              value="all"
              label={translate('All')}
              selected={platform === 'all'}
              onSelect={setPlatform}
            />
            {present.map((p) => (
              <PlatformButton
                key={p.key}
                value={p.key}
                label={p.name}
                selected={platform === p.key}
                onSelect={setPlatform}
              />
            ))}
          </div>
        ) : null}

        <div className={styles.grid}>
          {visible.map((game) => (
            <div key={game.id} className={styles.tile}>
              <Link
                className={styles.cover}
                to={`/games/${game.id}/play`}
                title={game.overview}
              >
                {game.hasCover ? (
                  <img
                    className={styles.coverImage}
                    src={getCoverUrl(game.id)}
                    alt={game.title}
                    loading="lazy"
                  />
                ) : (
                  game.title
                )}
                <span className={styles.platform}>{game.platformKey}</span>
              </Link>
              <div className={styles.title} title={game.title}>
                {game.title}
              </div>
              <div className={styles.info}>
                {[
                  game.platformName,
                  game.year,
                  game.genres?.split(',')[0],
                  formatBytes(game.size),
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </div>
            </div>
          ))}
        </div>

        <GameMetadataSettings />
      </PageContentBody>
    </PageContent>
  );
}

export default GamesIndex;
