import React, { useCallback, useMemo, useState } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import {
  getTimeUnit,
  seedingModeOptions,
  timeUnitOptions,
  usesRatio,
  usesTime,
} from './seedingRuleOptions';
import { SeedingRule, useSaveSeedingRule } from './useSeedingRules';

// Not a backend mode: the item has no goal of its own and uses the next level's.
const INHERIT = -1;

export interface SeedingRuleModalContentProps {
  seriesId: number;
  seasonNumber?: number;
  title: string;
  rule: SeedingRule | null;
  inheritLabel: string;
  onModalClose: () => void;
}

function SeedingRuleModalContent({
  seriesId,
  seasonNumber,
  title,
  rule,
  inheritLabel,
  onModalClose,
}: SeedingRuleModalContentProps) {
  const [mode, setMode] = useState(rule?.mode ?? INHERIT);
  const [minutes, setMinutes] = useState(rule?.minutes ?? 14 * 24 * 60);
  const [ratio, setRatio] = useState(rule?.ratio ?? 1);
  const [unit, setUnit] = useState(() => getTimeUnit(minutes));

  const { mutate, isPending, error } = useSaveSeedingRule(
    seriesId,
    seasonNumber
  );

  const modeOptions: EnhancedSelectInputValue<number>[] = useMemo(
    () => [{ key: INHERIT, value: inheritLabel }, ...seedingModeOptions],
    [inheritLabel]
  );

  const handleModeChange = useCallback(({ value }: InputChanged<number>) => {
    setMode(value);
  }, []);

  const handleTimeChange = useCallback(
    ({ value }: InputChanged<number | null>) => {
      setMinutes(Math.max(0, Math.round((value ?? 0) * unit)));
    },
    [unit]
  );

  const handleUnitChange = useCallback(
    ({ value }: InputChanged<number>) => {
      // Keeps the number shown and changes what it counts.
      setMinutes(Math.round((minutes / unit) * value));
      setUnit(value);
    },
    [minutes, unit]
  );

  const handleRatioChange = useCallback(
    ({ value }: InputChanged<number | null>) => {
      setRatio(Math.max(0, value ?? 0));
    },
    []
  );

  const handleSavePress = useCallback(() => {
    mutate(
      { rule: mode === INHERIT ? null : { mode, minutes, ratio } },
      { onSuccess: onModalClose }
    );
  }, [mode, minutes, ratio, mutate, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('SeedingGoalFor', { title })}</ModalHeader>

      <ModalBody>
        <Form>
          <FormRow>
            <FormLabel>{translate('EngineSeedingGoal')}</FormLabel>

            <FormInputHelpText
              text={translate('SeedingGoalOverrideHelpText')}
            />

            <FormInput
              type={inputTypes.SELECT}
              name="mode"
              values={modeOptions}
              value={mode}
              onChange={handleModeChange}
            />
          </FormRow>

          {mode !== INHERIT && usesTime(mode) ? (
            <FormRow>
              <FormLabel>{translate('EngineSeedingTime')}</FormLabel>

              <FormInput
                type={inputTypes.NUMBER}
                name="time"
                min={0}
                isFloat={true}
                value={minutes / unit}
                onChange={handleTimeChange}
              />
            </FormRow>
          ) : null}

          {mode !== INHERIT && usesTime(mode) ? (
            <FormRow>
              <FormLabel>{translate('EngineSeedingTimeUnit')}</FormLabel>

              <FormInput
                type={inputTypes.SELECT}
                name="timeUnit"
                values={timeUnitOptions}
                value={unit}
                onChange={handleUnitChange}
              />
            </FormRow>
          ) : null}

          {mode !== INHERIT && usesRatio(mode) ? (
            <FormRow>
              <FormLabel>{translate('EngineSeedingRatio')}</FormLabel>

              <FormInput
                type={inputTypes.NUMBER}
                name="ratio"
                min={0}
                step={0.1}
                isFloat={true}
                value={ratio}
                onChange={handleRatioChange}
              />
            </FormRow>
          ) : null}
        </Form>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          error={error}
          isSpinning={isPending}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default SeedingRuleModalContent;
