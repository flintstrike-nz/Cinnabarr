import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback } from 'react';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';

interface EngineLegalNotice {
  accepted: boolean;
  text: string;
}

// Shown once, until the owner accepts it: the built-in engine shares what it downloads.
function EngineLegalNoticeModal() {
  const queryClient = useQueryClient();
  const { data } = useApiQuery<EngineLegalNotice>({
    path: '/engine/legalnotice',
  });
  const { mutate } = useApiMutation<unknown, void>({
    path: '/engine/legalnotice',
    method: 'POST',
    mutationOptions: {
      onSuccess: () => {
        queryClient.invalidateQueries({ queryKey: ['/engine/legalnotice'] });
      },
    },
  });

  const handleConfirm = useCallback(() => {
    mutate();
  }, [mutate]);

  // The modal is only dismissed by accepting it.
  const handleCancel = useCallback(() => undefined, []);

  return (
    <ConfirmModal
      isOpen={!!data && !data.accepted}
      kind={kinds.PRIMARY}
      title="Before you download"
      message={data?.text}
      confirmLabel="I understand"
      hideCancelButton={true}
      onConfirm={handleConfirm}
      onCancel={handleCancel}
    />
  );
}

export default EngineLegalNoticeModal;
