import React, { useCallback, useEffect, useState } from 'react';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import { DoctorSettings, useDoctorSettings } from './useDoctor';

// When Doctor runs at night and how long a flagged file waits before it is deleted.
function DoctorSettingsPanel() {
  const { settings, save, isSaving, saveError } = useDoctorSettings();
  const [draft, setDraft] = useState<DoctorSettings | null>(null);

  useEffect(() => {
    if (settings) {
      setDraft(settings);
    }
  }, [settings]);

  const handleEnabledChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setDraft((d) => d && { ...d, enabled: event.target.checked });
    },
    []
  );

  const handleIntervalChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setDraft((d) => d && { ...d, intervalHours: Number(event.target.value) });
    },
    []
  );

  const handleRetentionChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setDraft((d) => d && { ...d, retentionDays: Number(event.target.value) });
    },
    []
  );

  const handleSavePress = useCallback(() => {
    if (draft) {
      save(draft);
    }
  }, [draft, save]);

  if (!draft) {
    return null;
  }

  return (
    <div>
      <h3>{translate('DoctorSchedule')}</h3>

      <p>
        <label>
          <input
            type="checkbox"
            checked={draft.enabled}
            onChange={handleEnabledChange}
          />{' '}
          {translate('DoctorEnabled')}
        </label>
      </p>

      <p>
        <label>
          {translate('DoctorIntervalHours')}{' '}
          <input
            type="number"
            min={1}
            value={draft.intervalHours}
            onChange={handleIntervalChange}
          />
        </label>
      </p>

      <p>
        <label>
          {translate('DoctorRetentionDays')}{' '}
          <input
            type="number"
            min={1}
            value={draft.retentionDays}
            onChange={handleRetentionChange}
          />
        </label>
      </p>

      <Button
        kind={kinds.PRIMARY}
        isDisabled={isSaving}
        onPress={handleSavePress}
      >
        {translate('Save')}
      </Button>

      {saveError ? <div>{getErrorMessage(saveError)}</div> : null}
    </div>
  );
}

export default DoctorSettingsPanel;
