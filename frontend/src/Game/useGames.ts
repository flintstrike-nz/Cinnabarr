import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import getQueryPath from 'Utilities/Fetch/getQueryPath';

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
  overview?: string;
  year?: number;
  genres?: string;
  developer?: string;
  rating?: number;
  hasCover: boolean;
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

export interface GameSettings {
  igdbClientId?: string;
  igdbClientSecret?: string;
  hasIgdbClientSecret: boolean;
}

export interface GameSettingsTest {
  ok: boolean;
  message?: string;
}

export const useGameSettings = () => {
  const queryClient = useQueryClient();
  const query = useApiQuery<GameSettings>({ path: '/game/settings' });

  const save = useApiMutation<GameSettings, GameSettings>({
    path: '/game/settings',
    method: 'PUT',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/game/settings'] });
      },
    },
  });

  const test = useApiMutation<GameSettingsTest, void>({
    path: '/game/settings/test',
    method: 'POST',
  });

  return {
    settings: query.data,
    save: save.mutate,
    isSaving: save.isPending,
    saveError: save.error,
    test: () => test.mutate(),
    isTesting: test.isPending,
    testResult: test.data,
  };
};

// An <img> cannot send headers, so the api key goes in the query.
export const getCoverUrl = (id: number) =>
  `${getQueryPath(`/game/${id}/cover`)}?apikey=${window.Sonarr.apiKey}`;
