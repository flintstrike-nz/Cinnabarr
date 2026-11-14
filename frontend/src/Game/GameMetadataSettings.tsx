import React, { useCallback, useEffect, useState } from 'react';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import { useGameSettings } from './useGames';

// Where the cover art and details come from: an IGDB key (a free Twitch application the owner registers).
function GameMetadataSettings() {
  const { settings, save, isSaving, saveError, test, isTesting, testResult } =
    useGameSettings();
  const executeCommand = useExecuteCommand();
  const [clientId, setClientId] = useState('');
  const [secret, setSecret] = useState('');

  useEffect(() => {
    if (settings) {
      setClientId(settings.igdbClientId ?? '');
    }
  }, [settings]);

  const handleClientIdChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) =>
      setClientId(event.target.value),
    []
  );

  const handleSecretChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) =>
      setSecret(event.target.value),
    []
  );

  const handleSavePress = useCallback(() => {
    save({
      igdbClientId: clientId,
      igdbClientSecret: secret,
      hasIgdbClientSecret: false,
    });
    setSecret('');
  }, [clientId, secret, save]);

  const handleRefreshPress = useCallback(() => {
    executeCommand({ name: CommandNames.RefreshGameMetadata });
  }, [executeCommand]);

  return (
    <div>
      <h3>{translate('GamesMetadata')}</h3>
      <p>{translate('GamesMetadataHelp')}</p>
      <p>
        <label>
          {translate('IgdbClientId')}{' '}
          <input value={clientId} onChange={handleClientIdChange} />
        </label>
      </p>
      <p>
        <label>
          {translate('IgdbClientSecret')}{' '}
          <input
            type="password"
            autoComplete="off"
            placeholder={settings?.hasIgdbClientSecret ? '••••••••' : ''}
            value={secret}
            onChange={handleSecretChange}
          />
        </label>
      </p>
      <Button
        kind={kinds.PRIMARY}
        isDisabled={isSaving}
        onPress={handleSavePress}
      >
        {translate('Save')}
      </Button>{' '}
      <Button isDisabled={isTesting} onPress={test}>
        {translate('TestConnection')}
      </Button>{' '}
      <Button onPress={handleRefreshPress}>
        {translate('GamesRefreshMetadata')}
      </Button>
      {testResult ? (
        <p>
          {testResult.ok ? translate('GamesIgdbWorks') : testResult.message}
        </p>
      ) : null}
      {saveError ? <div>{getErrorMessage(saveError)}</div> : null}
    </div>
  );
}

export default GameMetadataSettings;
