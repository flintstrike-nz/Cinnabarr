import React from 'react';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import EngineOrphansModalContent from './EngineOrphansModalContent';

interface EngineOrphansModalProps {
  isOpen: boolean;
  onModalClose: () => void;
}

function EngineOrphansModal({ isOpen, onModalClose }: EngineOrphansModalProps) {
  return (
    <Modal isOpen={isOpen} size={sizes.EXTRA_LARGE} onModalClose={onModalClose}>
      <EngineOrphansModalContent onModalClose={onModalClose} />
    </Modal>
  );
}

export default EngineOrphansModal;
