import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import translate from 'Utilities/String/translate';

// Matches EngineSeedingMode in Embarr's backend.
export const SEEDING_MODE_TIME = 1;
export const SEEDING_MODE_RATIO = 2;
export const SEEDING_MODE_EITHER = 3;
export const SEEDING_MODE_BOTH = 4;

export const seedingModeOptions: EnhancedSelectInputValue<number>[] = [
  {
    key: 0,
    get value() {
      return translate('EngineSeedingModeNone');
    },
  },
  {
    key: SEEDING_MODE_TIME,
    get value() {
      return translate('EngineSeedingModeTime');
    },
  },
  {
    key: SEEDING_MODE_RATIO,
    get value() {
      return translate('EngineSeedingModeRatio');
    },
  },
  {
    key: SEEDING_MODE_EITHER,
    get value() {
      return translate('EngineSeedingModeEither');
    },
  },
  {
    key: SEEDING_MODE_BOTH,
    get value() {
      return translate('EngineSeedingModeBoth');
    },
  },
];

// Minutes per unit; seeding times are stored in minutes.
const timeUnits = [
  { key: 43200, label: () => translate('EngineSeedingUnitMonths') },
  { key: 10080, label: () => translate('EngineSeedingUnitWeeks') },
  { key: 1440, label: () => translate('EngineSeedingUnitDays') },
  { key: 60, label: () => translate('EngineSeedingUnitHours') },
];

export const timeUnitOptions: EnhancedSelectInputValue<number>[] =
  timeUnits.map((unit) => ({
    key: unit.key,
    get value() {
      return unit.label();
    },
  }));

// The largest unit the stored minutes are a whole number of.
export function getTimeUnit(minutes: number) {
  return timeUnits.find((u) => minutes > 0 && minutes % u.key === 0)?.key ?? 60;
}

export function usesTime(mode: number) {
  return (
    mode === SEEDING_MODE_TIME ||
    mode === SEEDING_MODE_EITHER ||
    mode === SEEDING_MODE_BOTH
  );
}

export function usesRatio(mode: number) {
  return (
    mode === SEEDING_MODE_RATIO ||
    mode === SEEDING_MODE_EITHER ||
    mode === SEEDING_MODE_BOTH
  );
}
