import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import PageSectionContent from 'Components/Page/PageSectionContent';
import AddCard from 'Components/SettingsCard/AddCard';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import { kinds, sizes } from 'Helpers/Props';
import { SelectedSchema } from 'Settings/useProviderSchema';
import translate from 'Utilities/String/translate';
import EditIndexerProxyModalContent from './EditIndexerProxyModalContent';
import IndexerProxyCard from './IndexerProxyCard';
import {
  useIndexerProxies,
  useIndexerProxySchema,
  useSortedIndexerProxies,
} from './useIndexerProxies';

interface AddProxyButtonProps extends SelectedSchema {
  onSelect: (selected: SelectedSchema) => void;
}

function AddProxyButton({
  implementation,
  implementationName,
  onSelect,
}: AddProxyButtonProps) {
  const handlePress = useCallback(() => {
    onSelect({ implementation, implementationName });
  }, [implementation, implementationName, onSelect]);

  return <Button onPress={handlePress}>{implementationName}</Button>;
}

function IndexerProxies() {
  const { error, isFetching, isFetched } = useIndexerProxies();
  const items = useSortedIndexerProxies();

  const [isAddOpen, setIsAddOpen] = useState(false);
  const [isEditOpen, setIsEditOpen] = useState(false);
  const [selectedSchema, setSelectedSchema] = useState<
    SelectedSchema | undefined
  >(undefined);

  const { isSchemaLoading, isSchemaFetched, schemaError, schema } =
    useIndexerProxySchema(isAddOpen);

  const handleAddPress = useCallback(() => setIsAddOpen(true), []);
  const handleAddClose = useCallback(() => setIsAddOpen(false), []);
  const handleEditClose = useCallback(() => setIsEditOpen(false), []);

  const handleSelect = useCallback((selected: SelectedSchema) => {
    setSelectedSchema(selected);
    setIsAddOpen(false);
    setIsEditOpen(true);
  }, []);

  return (
    <PageSectionContent
      errorMessage={translate('IndexerProxiesLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      <div className={settingsCardStyles.grid}>
        {items.map((item) => (
          <IndexerProxyCard key={item.id} {...item} />
        ))}

        <AddCard
          label={translate('AddIndexerProxy')}
          onPress={handleAddPress}
        />
      </div>

      <Modal
        size={sizes.MEDIUM}
        isOpen={isAddOpen}
        onModalClose={handleAddClose}
      >
        <ModalContent onModalClose={handleAddClose}>
          <ModalHeader>{translate('AddIndexerProxy')}</ModalHeader>

          <ModalBody>
            {isSchemaLoading && !isSchemaFetched ? <LoadingIndicator /> : null}

            {!isSchemaLoading && !!schemaError ? (
              <Alert kind={kinds.DANGER}>
                {translate('AddIndexerProxyError')}
              </Alert>
            ) : null}

            {isSchemaFetched && !schemaError
              ? schema.map((proxy) => (
                  <AddProxyButton
                    key={proxy.implementation}
                    implementation={proxy.implementation}
                    implementationName={proxy.implementationName}
                    onSelect={handleSelect}
                  />
                ))
              : null}
          </ModalBody>

          <ModalFooter>
            <Button onPress={handleAddClose}>{translate('Close')}</Button>
          </ModalFooter>
        </ModalContent>
      </Modal>

      <Modal
        size={sizes.MEDIUM}
        isOpen={isEditOpen}
        onModalClose={handleEditClose}
      >
        <EditIndexerProxyModalContent
          selectedSchema={selectedSchema}
          onModalClose={handleEditClose}
        />
      </Modal>
    </PageSectionContent>
  );
}

export default IndexerProxies;
