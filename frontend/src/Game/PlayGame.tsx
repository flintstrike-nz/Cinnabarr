import React, { useEffect } from 'react';
import { useParams } from 'react-router-dom';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import getQueryPath from 'Utilities/Fetch/getQueryPath';
import translate from 'Utilities/String/translate';
import { useGames } from './useGames';
import styles from './PlayGame.module.css';

// Where the emulator comes from. Cinnabarr does not ship it: the browser loads it from the EmulatorJS site.
const EMULATOR_DATA = 'https://cdn.emulatorjs.org/stable/data/';

interface PlayInfo {
  playable: boolean;
  reason?: string;
  core?: string;
  hasState: boolean;
  needsBios: boolean;
  hasBios: boolean;
}

const withKey = (path: string) =>
  `${getQueryPath(path)}?apikey=${window.Sonarr.apiKey}`;

// Play a game in the browser (EmulatorJS): keyboard or a gamepad, and the save state is kept on the server.
function PlayGame() {
  const { id } = useParams();
  const gameId = Number(id);
  const { data: games } = useGames();
  const game = games.find((g) => g.id === gameId);
  const { data: info } = useApiQuery<PlayInfo>({
    path: `/game/${gameId}/play`,
    queryOptions: { enabled: gameId > 0, refetchOnWindowFocus: false },
  });

  useEffect(() => {
    if (!info?.playable || !game) {
      return undefined;
    }

    const w = window as unknown as Record<string, unknown>;

    w.EJS_player = '#game';
    w.EJS_core = info.core;
    w.EJS_gameName = game.title;
    w.EJS_gameUrl = withKey(`/game/${gameId}/rom`);
    w.EJS_pathtodata = EMULATOR_DATA;

    if (info.hasState) {
      w.EJS_loadStateURL = withKey(`/game/${gameId}/state`);
    }

    if (info.hasBios) {
      w.EJS_biosUrl = withKey(`/game/bios/${game.platformKey}`);
    }

    // Keep the save state on the server rather than only in this browser.
    w.EJS_onSaveState = ({ state }: { state: Uint8Array }) => {
      fetch(getQueryPath(`/game/${gameId}/state`), {
        method: 'PUT',
        headers: {
          'X-Api-Key': window.Sonarr.apiKey,
          'Content-Type': 'application/octet-stream',
        },
        body: new Blob([state as unknown as BlobPart]),
      });
    };

    const script = document.createElement('script');
    script.src = `${EMULATOR_DATA}loader.js`;
    document.body.appendChild(script);

    return () => {
      script.remove();
    };
  }, [info, game, gameId]);

  return (
    <PageContent title={game?.title ?? translate('Games')}>
      <PageContentBody>
        <p>
          <a href={`${window.Sonarr.urlBase}/games`}>← {translate('Games')}</a>
        </p>

        {info && !info.playable ? (
          <div className={styles.notice}>{info.reason}</div>
        ) : null}

        {info?.needsBios && !info.hasBios ? (
          <div className={styles.notice}>{translate('GamesNeedsBios')}</div>
        ) : null}

        {info?.playable ? <div id="game" className={styles.player} /> : null}

        {info?.playable ? <p>{translate('GamesPlayHelp')}</p> : null}
      </PageContentBody>
    </PageContent>
  );
}

export default PlayGame;
