import React, { useCallback, useState } from 'react';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Modal from 'Components/Modal/Modal';
import SettingsCard from 'Components/SettingsCard/SettingsCard';
import SettingsCardStatus from 'Components/SettingsCard/SettingsCardStatus';
import TagList from 'Components/TagList';
import { kinds, sizes } from 'Helpers/Props';
import { useTagList } from 'Tags/useTags';
import translate from 'Utilities/String/translate';
import EditIndexerProxyModalContent from './EditIndexerProxyModalContent';
import { IndexerProxyModel, useDeleteIndexerProxy } from './useIndexerProxies';

function IndexerProxyCard({
  id,
  name,
  implementationName,
  tags,
}: IndexerProxyModel) {
  const tagList = useTagList();
  const { deleteIndexerProxy } = useDeleteIndexerProxy(id);

  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);

  const handleEditPress = useCallback(() => setIsEditModalOpen(true), []);
  const handleEditClose = useCallback(() => setIsEditModalOpen(false), []);

  const handleDeletePress = useCallback(() => {
    setIsEditModalOpen(false);
    setIsDeleteModalOpen(true);
  }, []);

  const handleDeleteClose = useCallback(() => setIsDeleteModalOpen(false), []);

  const hasTags = tags.length > 0;

  return (
    <SettingsCard
      name={name}
      aria-label={translate('EditIndexerProxyImplementation', {
        implementationName: name,
      })}
      onPress={handleEditPress}
    >
      <SettingsCardStatus
        dot={hasTags ? 'active' : 'muted'}
        segments={[
          implementationName,
          translate(hasTags ? 'Enabled' : 'Disabled'),
        ]}
      />

      {hasTags ? <TagList tags={tags} tagList={tagList} /> : null}

      <Modal
        size={sizes.MEDIUM}
        isOpen={isEditModalOpen}
        onModalClose={handleEditClose}
      >
        <EditIndexerProxyModalContent
          id={id}
          onModalClose={handleEditClose}
          onDeletePress={handleDeletePress}
        />
      </Modal>

      <ConfirmModal
        isOpen={isDeleteModalOpen}
        kind={kinds.DANGER}
        title={translate('DeleteIndexerProxy')}
        message={translate('DeleteIndexerProxyMessageText', { name })}
        confirmLabel={translate('Delete')}
        onConfirm={deleteIndexerProxy}
        onCancel={handleDeleteClose}
      />
    </SettingsCard>
  );
}

export default IndexerProxyCard;
