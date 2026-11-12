import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useEffect, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import SpinnerButton from 'Components/Link/SpinnerButton';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';

interface EnginePreferences {
  listenPort: number;
  randomPort: boolean;
  upnp: boolean;
  networkInterface: string;
  downloadLimit: number;
  uploadLimit: number;
  alternativeDownloadLimit: number;
  alternativeUploadLimit: number;
  schedulerEnabled: boolean;
  scheduleFromHour: number;
  scheduleFromMinute: number;
  scheduleToHour: number;
  scheduleToMinute: number;
  schedulerDays: number;
  queueingEnabled: boolean;
  maxActiveDownloads: number;
  maxActiveUploads: number;
  maxActiveTorrents: number;
  dht: boolean;
  pex: boolean;
  lsd: boolean;
  anonymousMode: boolean;
  encryption: number;
  preallocateDisk: boolean;
}

type NumberKey =
  | 'listenPort'
  | 'downloadLimit'
  | 'uploadLimit'
  | 'alternativeDownloadLimit'
  | 'alternativeUploadLimit'
  | 'scheduleFromHour'
  | 'scheduleFromMinute'
  | 'scheduleToHour'
  | 'scheduleToMinute'
  | 'schedulerDays'
  | 'maxActiveDownloads'
  | 'maxActiveUploads'
  | 'maxActiveTorrents'
  | 'encryption';

type BooleanKey =
  | 'randomPort'
  | 'upnp'
  | 'schedulerEnabled'
  | 'queueingEnabled'
  | 'dht'
  | 'pex'
  | 'lsd'
  | 'anonymousMode'
  | 'preallocateDisk';

const NUMBER_FIELDS: { key: NumberKey; label: string; help?: string }[] = [
  { key: 'listenPort', label: 'EngineNetworkListenPort' },
  {
    key: 'downloadLimit',
    label: 'EngineNetworkDownloadLimit',
    help: 'EngineNetworkLimitHelpText',
  },
  {
    key: 'uploadLimit',
    label: 'EngineNetworkUploadLimit',
    help: 'EngineNetworkLimitHelpText',
  },
  { key: 'alternativeDownloadLimit', label: 'EngineNetworkAltDownloadLimit' },
  { key: 'alternativeUploadLimit', label: 'EngineNetworkAltUploadLimit' },
  { key: 'scheduleFromHour', label: 'EngineNetworkScheduleFromHour' },
  { key: 'scheduleFromMinute', label: 'EngineNetworkScheduleFromMinute' },
  { key: 'scheduleToHour', label: 'EngineNetworkScheduleToHour' },
  { key: 'scheduleToMinute', label: 'EngineNetworkScheduleToMinute' },
  {
    key: 'maxActiveDownloads',
    label: 'EngineNetworkMaxActiveDownloads',
    help: 'EngineNetworkQueueHelpText',
  },
  { key: 'maxActiveUploads', label: 'EngineNetworkMaxActiveUploads' },
  { key: 'maxActiveTorrents', label: 'EngineNetworkMaxActiveTorrents' },
];

const SCHEDULE_DAYS = [
  'EngineNetworkScheduleEveryDay',
  'EngineNetworkScheduleWeekdays',
  'EngineNetworkScheduleWeekends',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
];

const BOOLEAN_FIELDS: { key: BooleanKey; label: string }[] = [
  { key: 'randomPort', label: 'EngineNetworkRandomPort' },
  { key: 'upnp', label: 'EngineNetworkUpnp' },
  { key: 'schedulerEnabled', label: 'EngineNetworkScheduler' },
  { key: 'queueingEnabled', label: 'EngineNetworkQueueing' },
  { key: 'dht', label: 'EngineNetworkDht' },
  { key: 'pex', label: 'EngineNetworkPex' },
  { key: 'lsd', label: 'EngineNetworkLsd' },
  { key: 'anonymousMode', label: 'EngineNetworkAnonymousMode' },
  { key: 'preallocateDisk', label: 'EngineNetworkPreallocate' },
];

// The engine's own network, speed, queue and privacy settings (roadmap SET-04). They live in the engine, so this section
// saves on its own, straight to it.
function EngineNetworkOptions() {
  const queryClient = useQueryClient();
  const { data, error } = useApiQuery<EnginePreferences>({
    path: '/engine/preferences',
    queryOptions: { refetchOnWindowFocus: false, retry: false },
  });
  const [values, setValues] = useState<EnginePreferences | null>(null);

  const {
    mutate: save,
    isPending,
    error: saveError,
  } = useApiMutation<unknown, EnginePreferences>({
    path: '/engine/preferences',
    method: 'PUT',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/engine/preferences'] });
      },
    },
  });

  useEffect(() => {
    if (data) {
      setValues(data);
    }
  }, [data]);

  const handleNumberChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const key = event.target.name as NumberKey;
      const value = Number(event.target.value);

      setValues((current) =>
        current ? { ...current, [key]: value } : current
      );
    },
    []
  );

  const handleBooleanChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const key = event.target.name as BooleanKey;
      const { checked } = event.target;

      setValues((current) =>
        current ? { ...current, [key]: checked } : current
      );
    },
    []
  );

  const handleInterfaceChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const { value } = event.target;

      setValues((current) =>
        current ? { ...current, networkInterface: value } : current
      );
    },
    []
  );

  const handleDaysChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const schedulerDays = Number(event.target.value);

      setValues((current) =>
        current ? { ...current, schedulerDays } : current
      );
    },
    []
  );

  const handleEncryptionChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const encryption = Number(event.target.value);

      setValues((current) => (current ? { ...current, encryption } : current));
    },
    []
  );

  const handleSavePress = useCallback(() => {
    if (values) {
      save(values);
    }
  }, [values, save]);

  // Without the engine (not running, or still starting) there is nothing to edit.
  if (error || !values) {
    return error ? (
      <FieldSet legend={translate('EngineNetwork')}>
        <p>{getErrorMessage(error)}</p>
      </FieldSet>
    ) : null;
  }

  return (
    <FieldSet
      legend={translate('EngineNetwork')}
      caption={translate('EngineNetworkCaption')}
    >
      <div>
        {NUMBER_FIELDS.map(({ key, label, help }) => (
          <p key={key}>
            <label>
              {translate(label)}{' '}
              <input
                type="number"
                name={key}
                value={values[key]}
                onChange={handleNumberChange}
              />
            </label>{' '}
            {help ? <small>{translate(help)}</small> : null}
          </p>
        ))}

        <p>
          <label>
            {translate('EngineNetworkInterface')}{' '}
            <input
              type="text"
              name="networkInterface"
              value={values.networkInterface}
              onChange={handleInterfaceChange}
            />
          </label>{' '}
          <small>{translate('EngineNetworkInterfaceHelpText')}</small>
        </p>

        <p>
          <label>
            {translate('EngineNetworkScheduleDays')}{' '}
            <select value={values.schedulerDays} onChange={handleDaysChange}>
              {SCHEDULE_DAYS.map((day, index) => (
                <option key={day} value={index}>
                  {translate(day)}
                </option>
              ))}
            </select>
          </label>
        </p>

        <p>
          <label>
            {translate('EngineNetworkEncryption')}{' '}
            <select value={values.encryption} onChange={handleEncryptionChange}>
              <option value={0}>
                {translate('EngineNetworkEncryptionPrefer')}
              </option>
              <option value={1}>
                {translate('EngineNetworkEncryptionRequire')}
              </option>
              <option value={2}>
                {translate('EngineNetworkEncryptionOff')}
              </option>
            </select>
          </label>
        </p>

        {BOOLEAN_FIELDS.map(({ key, label }) => (
          <p key={key}>
            <label>
              <input
                type="checkbox"
                name={key}
                checked={values[key]}
                onChange={handleBooleanChange}
              />{' '}
              {translate(label)}
            </label>
          </p>
        ))}

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isPending}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerButton>

        {saveError ? <p>{getErrorMessage(saveError)}</p> : null}
      </div>
    </FieldSet>
  );
}

export default EngineNetworkOptions;
