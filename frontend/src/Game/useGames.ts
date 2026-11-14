import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface Game {
  id: number;
  platformKey: string;
  platformName?: string;
  title: string;
  sortTitle?: string;
  region?: string;
  rootFolderPath?: string;
  path?: string;
  size: number;
  added: string;
}

export interface GamePlatform {
  key: string;
  name: string;
}

const NO_GAMES: Game[] = [];
const NO_PLATFORMS: GamePlatform[] = [];

export const useGames = () => {
  const result = useApiQuery<Game[]>({ path: '/game' });

  return { ...result, data: result.data ?? NO_GAMES };
};

export const useGamePlatforms = () => {
  const result = useApiQuery<GamePlatform[]>({ path: '/game/platform' });

  return { ...result, data: result.data ?? NO_PLATFORMS };
};
