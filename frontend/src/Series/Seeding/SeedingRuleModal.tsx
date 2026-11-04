import React from 'react';
import Modal from 'Components/Modal/Modal';
import SeedingRuleModalContent, {
  SeedingRuleModalContentProps,
} from './SeedingRuleModalContent';

interface SeedingRuleModalProps extends SeedingRuleModalContentProps {
  isOpen: boolean;
}

function SeedingRuleModal({
  isOpen,
  onModalClose,
  ...otherProps
}: SeedingRuleModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <SeedingRuleModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default SeedingRuleModal;
