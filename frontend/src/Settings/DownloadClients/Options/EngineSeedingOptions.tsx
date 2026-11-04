import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import { inputTypes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { DownloadClientSettingsModel } from './useDownloadClientSettings';

// Matches EngineSeedingMode in Embarr's backend.
const seedingModeOptions: EnhancedSelectInputValue<number>[] = [
  {
    key: 0,
    get value() {
      return translate('EngineSeedingModeNone');
    },
  },
  {
    key: 1,
    get value() {
      return translate('EngineSeedingModeTime');
    },
  },
  {
    key: 2,
    get value() {
      return translate('EngineSeedingModeRatio');
    },
  },
  {
    key: 3,
    get value() {
      return translate('EngineSeedingModeEither');
    },
  },
  {
    key: 4,
    get value() {
      return translate('EngineSeedingModeBoth');
    },
  },
];

// Minutes per unit; the time is stored in minutes.
const timeUnits = [
  { key: 43200, label: () => translate('EngineSeedingUnitMonths') },
  { key: 10080, label: () => translate('EngineSeedingUnitWeeks') },
  { key: 1440, label: () => translate('EngineSeedingUnitDays') },
  { key: 60, label: () => translate('EngineSeedingUnitHours') },
];

const timeUnitOptions: EnhancedSelectInputValue<number>[] = timeUnits.map(
  (unit) => ({
    key: unit.key,
    get value() {
      return unit.label();
    },
  })
);

// The largest unit the stored minutes are a whole number of.
function getUnit(minutes: number) {
  return timeUnits.find((u) => minutes > 0 && minutes % u.key === 0)?.key ?? 60;
}

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
  const [unit, setUnit] = useState(() => getUnit(minutes));

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

  const usesTime = mode === 1 || mode === 3 || mode === 4;
  const usesRatio = mode === 2 || mode === 3 || mode === 4;

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
              value={minutes / unit}
              errors={settings.engineSeedingMinutes.errors}
              warnings={settings.engineSeedingMinutes.warnings}
              onChange={handleTimeChange}
            />

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
