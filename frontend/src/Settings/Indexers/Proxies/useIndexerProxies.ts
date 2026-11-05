import { useMemo } from 'react';
import {
  SelectedSchema,
  useProviderSchema,
  useSelectedSchema,
} from 'Settings/useProviderSchema';
import {
  useDeleteProvider,
  useManageProviderSettings,
  useProviderSettings,
} from 'Settings/useProviderSettings';
import Provider from 'typings/Provider';
import { sortByProp } from 'Utilities/Array/sortByProp';

export interface IndexerProxyModel extends Provider {
  tags: number[];
}

const PATH = '/indexerproxy';

export const useIndexerProxies = () => {
  return useProviderSettings<IndexerProxyModel>({ path: PATH });
};

export const useSortedIndexerProxies = () => {
  const { data } = useIndexerProxies();

  return useMemo(() => [...data].sort(sortByProp('name')), [data]);
};

export const useManageIndexerProxy = (
  id: number | undefined,
  selectedSchema?: SelectedSchema
) => {
  const schema = useSelectedSchema<IndexerProxyModel>(PATH, selectedSchema);

  if (selectedSchema && !schema) {
    throw new Error('A selected schema is required to manage a proxy');
  }

  return useManageProviderSettings<IndexerProxyModel>(
    id,
    selectedSchema && schema
      ? { ...schema, name: schema.implementationName || '' }
      : ({} as IndexerProxyModel),
    PATH
  );
};

export const useDeleteIndexerProxy = (id: number) => {
  const result = useDeleteProvider<IndexerProxyModel>(id, PATH);

  return { ...result, deleteIndexerProxy: result.deleteProvider };
};

export const useIndexerProxySchema = (enabled: boolean = true) => {
  return useProviderSchema<IndexerProxyModel>(PATH, enabled);
};
