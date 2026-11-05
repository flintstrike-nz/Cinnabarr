import { useMemo } from 'react';
import ModelBase from 'App/ModelBase';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import Provider from 'typings/Provider';

type ProviderWithPresets<T> = T & {
  presets: T[];
};

export interface SelectedSchema {
  implementation: string;
  implementationName: string;
  presetName?: string;
}

export const useProviderSchema = <T extends ModelBase>(
  path: string,
  enabled: boolean = true
) => {
  const { isLoading, isFetched, error, data } = useApiQuery<T[]>({
    path: `${path}/schema`,
    queryOptions: {
      enabled,
    },
  });

  return {
    isSchemaLoading: isLoading,
    isSchemaFetched: isFetched,
    schemaError: error,
    schema: data ?? ([] as T[]),
  };
};

export const useSelectedSchema = <T extends Provider>(
  path: string,
  selectedSchema: SelectedSchema | undefined
) => {
  const { schema } = useProviderSchema<T>(path, selectedSchema != null);

  return useMemo(() => {
    if (!selectedSchema) {
      return undefined;
    }

    // Several entries can share one implementation (indexers added from a definition file),
    // so prefer the one with the same name.
    const sameImplementation = schema.filter(
      (s: T) => s.implementation === selectedSchema.implementation
    );
    const selected =
      sameImplementation.find(
        (s: T) =>
          'implementationName' in s &&
          s.implementationName === selectedSchema.implementationName
      ) ?? sameImplementation[0];

    if (!selected) {
      throw new Error(
        `Schema with implementation ${selectedSchema.implementation} not found`
      );
    }

    if (selectedSchema.presetName == null) {
      return selected;
    }

    const preset =
      'presets' in selected
        ? (selected as ProviderWithPresets<T>).presets?.find(
            (p: T & { name: string }) => p.name === selectedSchema.presetName
          )
        : undefined;

    if (!preset) {
      throw new Error(
        `Preset with name ${selectedSchema.presetName} not found for implementation ${selectedSchema.implementation}`
      );
    }

    return preset;
  }, [schema, selectedSchema]);
};
