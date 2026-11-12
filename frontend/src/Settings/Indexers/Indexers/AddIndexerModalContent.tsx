import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import { IndexerModel, useIndexerSchema } from '../useIndexers';
import AddIndexerItem from './AddIndexerItem';
import styles from './AddIndexerModalContent.module.css';

export interface AddIndexerModalContentProps {
  onIndexerSelect: (selectedSchema: SelectedSchema) => void;
  onModalClose: () => void;
}

function AddIndexerModalContent({
  onIndexerSelect,
  onModalClose,
}: AddIndexerModalContentProps) {
  const { isSchemaLoading, isSchemaFetched, schemaError, schema } =
    useIndexerSchema();

  const [filter, setFilter] = useState('');

  // Cinnabarr only handles torrents.
  const torrentIndexers = useMemo(() => {
    const text = filter.trim().toLowerCase();

    return schema.filter(
      (item: IndexerModel) =>
        item.protocol === 'torrent' &&
        (!text || item.implementationName.toLowerCase().includes(text))
    );
  }, [schema, filter]);

  const handleFilterChange = useCallback(
    ({ value }: { value: string }) => setFilter(value),
    []
  );

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddIndexer')}</ModalHeader>

      <ModalBody>
        {isSchemaLoading ? <LoadingIndicator /> : null}

        {!isSchemaLoading && !!schemaError ? (
          <Alert kind={kinds.DANGER}>{translate('AddIndexerError')}</Alert>
        ) : null}

        {isSchemaFetched && !schemaError ? (
          <div className={styles.indexers}>
            <div className={styles.filter}>
              <TextInput
                name="filter"
                placeholder={translate('Filter')}
                value={filter}
                autoFocus={true}
                onChange={handleFilterChange}
              />
            </div>

            <div className={styles.group}>
              <h3 className={styles.groupHeading}>{translate('Torrents')}</h3>

              {torrentIndexers.map((indexer) => (
                <AddIndexerItem
                  key={`${indexer.implementation}-${indexer.implementationName}`}
                  {...indexer}
                  implementation={indexer.implementation}
                  onIndexerSelect={onIndexerSelect}
                />
              ))}
            </div>
          </div>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddIndexerModalContent;
