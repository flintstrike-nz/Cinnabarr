import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback, useEffect, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';

interface DefaultVersion {
  label: string;
  qualityProfileId: number;
}

const NO_VERSIONS: DefaultVersion[] = [];

interface RowProps {
  index: number;
  version: DefaultVersion;
  onChange: (index: number, version: DefaultVersion) => void;
  onRemove: (index: number) => void;
}

function DefaultVersionRow({ index, version, onChange, onRemove }: RowProps) {
  const profiles = useQualityProfilesData();

  const handleLabelChange = useCallback(
    ({ value }: InputChanged<string>) => {
      onChange(index, { ...version, label: value });
    },
    [index, version, onChange]
  );

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      onChange(index, {
        ...version,
        qualityProfileId: Number(event.target.value),
      });
    },
    [index, version, onChange]
  );

  const handleRemovePress = useCallback(() => {
    onRemove(index);
  }, [index, onRemove]);

  return (
    <div>
      <TextInput
        name={`defaultVersionLabel${index}`}
        value={version.label}
        placeholder={translate('VersionLabelPlaceholder')}
        onChange={handleLabelChange}
      />

      <select
        aria-label={translate('QualityProfile')}
        value={version.qualityProfileId}
        onChange={handleProfileChange}
      >
        {profiles.map((p) => (
          <option key={p.id} value={p.id}>
            {p.name}
          </option>
        ))}
      </select>

      <Button kind={kinds.DANGER} onPress={handleRemovePress}>
        {translate('Remove')}
      </Button>
    </div>
  );
}

interface DefaultVersionsProps {
  path: string;
  legend: string;
  caption: string;
}

// The versions every newly added movie or series gets (roadmap Phase 5), saved on their own.
function DefaultVersions({ path, legend, caption }: DefaultVersionsProps) {
  const queryClient = useQueryClient();
  const profiles = useQualityProfilesData();
  const { data, error: loadError } = useApiQuery<DefaultVersion[]>({
    path,
  });
  const [versions, setVersions] = useState<DefaultVersion[]>(NO_VERSIONS);
  const [isDirty, setIsDirty] = useState(false);

  const { isPending, error, mutate } = useApiMutation<
    DefaultVersion[],
    DefaultVersion[]
  >({
    path,
    method: 'PUT',
    mutationOptions: {
      onSuccess: () => {
        setIsDirty(false);
        queryClient.invalidateQueries({ queryKey: [path] });
      },
    },
  });

  useEffect(() => {
    if (data && !isDirty) {
      setVersions([...data]);
    }
  }, [data, isDirty]);

  const handleChange = useCallback((index: number, version: DefaultVersion) => {
    setIsDirty(true);
    setVersions((current) =>
      current.map((v, i) => (i === index ? version : v))
    );
  }, []);

  const handleRemove = useCallback((index: number) => {
    setIsDirty(true);
    setVersions((current) => current.filter((_, i) => i !== index));
  }, []);

  const handleAddPress = useCallback(() => {
    setIsDirty(true);
    setVersions((current) => [
      ...current,
      { label: '', qualityProfileId: profiles[0]?.id ?? 0 },
    ]);
  }, [profiles]);

  const handleSavePress = useCallback(() => {
    mutate(versions);
  }, [mutate, versions]);

  return (
    <FieldSet legend={legend} caption={caption}>
      {versions.map((version, index) => (
        <DefaultVersionRow
          // eslint-disable-next-line react/no-array-index-key
          key={index}
          index={index}
          version={version}
          onChange={handleChange}
          onRemove={handleRemove}
        />
      ))}

      <Button onPress={handleAddPress}>{translate('AddVersion')}</Button>

      <Button
        kind={kinds.PRIMARY}
        isDisabled={!isDirty || isPending}
        onPress={handleSavePress}
      >
        {translate('Save')}
      </Button>

      {loadError ? <p>{getErrorMessage(loadError)}</p> : null}
      {error ? <p>{getErrorMessage(error)}</p> : null}
    </FieldSet>
  );
}

export default DefaultVersions;
