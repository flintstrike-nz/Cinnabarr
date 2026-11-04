import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { inputTypes } from 'Helpers/Props';
import {
  getTimeUnit,
  seedingModeOptions,
  timeUnitOptions,
  usesRatio as modeUsesRatio,
  usesTime as modeUsesTime,
} from 'Series/Seeding/seedingRuleOptions';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { DownloadClientSettingsModel } from './useDownloadClientSettings';

interface EngineSeedingOptionsProps {
  settings: PendingSection<DownloadClientSettingsModel>;
  onInputChange: (change: InputChanged) => void;
}

function EngineSeedingOptions({
  settings,
  onInputChange,
}: EngineSeedingOptionsProps) {
  const minutes = settings.engineSeedingMinutes.value;
  const mode = settings.engineSeedingMode.value;
  const [unit, setUnit] = useState(() => getTimeUnit(minutes));

  const handleTimeChange = useCallback(
    ({ value }: InputChanged<number | null>) => {
      onInputChange({
        name: 'engineSeedingMinutes',
        value: Math.max(0, Math.round((value ?? 0) * unit)),
      });
    },
    [unit, onInputChange]
  );

  const handleUnitChange = useCallback(
    ({ value }: InputChanged<number>) => {
      // Keeps the number shown and changes what it counts.
      onInputChange({
        name: 'engineSeedingMinutes',
        value: Math.round((minutes / unit) * value),
      });
      setUnit(value);
    },
    [minutes, unit, onInputChange]
  );

  const usesTime = modeUsesTime(mode);
  const usesRatio = modeUsesRatio(mode);

  return (
    <FieldSet
      legend={translate('EngineSeeding')}
      caption={translate('EngineSeedingCaption')}
    >
      <Form>
        <FormRow>
          <FormLabel>{translate('EngineSeedingGoal')}</FormLabel>

          <FormInputHelpText text={translate('EngineSeedingGoalHelpText')} />

          <FormInput
            type={inputTypes.SELECT}
            name="engineSeedingMode"
            values={seedingModeOptions}
            onChange={onInputChange}
            {...settings.engineSeedingMode}
          />
        </FormRow>

        {usesTime ? (
          <FormRow>
            <FormLabel>{translate('EngineSeedingTime')}</FormLabel>

            <FormInput
              type={inputTypes.NUMBER}
              name="engineSeedingTime"
              min={0}
              isFloat={true}
              value={minutes / unit}
              errors={settings.engineSeedingMinutes.errors}
              warnings={settings.engineSeedingMinutes.warnings}
              onChange={handleTimeChange}
            />
          </FormRow>
        ) : null}

        {usesTime ? (
          <FormRow>
            <FormLabel>{translate('EngineSeedingTimeUnit')}</FormLabel>

            <FormInput
              type={inputTypes.SELECT}
              name="engineSeedingTimeUnit"
              values={timeUnitOptions}
              value={unit}
              onChange={handleUnitChange}
            />
          </FormRow>
        ) : null}

        {usesRatio ? (
          <FormRow>
            <FormLabel>{translate('EngineSeedingRatio')}</FormLabel>

            <FormInput
              type={inputTypes.NUMBER}
              name="engineSeedingRatio"
              min={0}
              step={0.1}
              isFloat={true}
              onChange={onInputChange}
              {...settings.engineSeedingRatio}
            />
          </FormRow>
        ) : null}
      </Form>
    </FieldSet>
  );
}

export default EngineSeedingOptions;
