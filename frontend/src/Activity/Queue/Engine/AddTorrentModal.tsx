import React, { ChangeEvent, useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useAddMagnet, useAddTorrentFile } from './useAddTorrent';
import { getEngineErrorMessage } from './useEngineControls';

interface AddTorrentModalContentProps {
  onModalClose: () => void;
}

function AddTorrentModalContent({ onModalClose }: AddTorrentModalContentProps) {
  const [magnetLink, setMagnetLink] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const addMagnet = useAddMagnet();
  const addFile = useAddTorrentFile();

  const handleMagnetChange = useCallback(({ value }: InputChanged<string>) => {
    setMagnetLink(value);
  }, []);

  const handleFileChange = useCallback(
    (event: ChangeEvent<HTMLInputElement>) => {
      setFile(event.target.files?.[0] ?? null);
    },
    []
  );

  const handleAddPress = useCallback(() => {
    if (file) {
      addFile.mutate(file, { onSuccess: onModalClose });
    } else {
      addMagnet.mutate({ magnetLink }, { onSuccess: onModalClose });
    }
  }, [file, magnetLink, addFile, addMagnet, onModalClose]);

  const error =
    getEngineErrorMessage(addMagnet.error) ?? addFile.error?.message;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('AddTorrent')}</ModalHeader>

      <ModalBody>
        <Alert kind={kinds.INFO}>{translate('AddTorrentHelpText')}</Alert>

        <Form>
          <FormRow>
            <FormLabel>{translate('MagnetLink')}</FormLabel>

            <FormInput
              type={inputTypes.TEXT}
              name="magnetLink"
              value={magnetLink}
              placeholder="magnet:?xt=urn:btih:…"
              onChange={handleMagnetChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('TorrentFile')}</FormLabel>

            <div>
              <FormInputHelpText text={translate('TorrentFileHelpText')} />
              <input
                type="file"
                accept=".torrent,application/x-bittorrent"
                onChange={handleFileChange}
              />
            </div>
          </FormRow>
        </Form>

        {error ? <Alert kind={kinds.DANGER}>{error}</Alert> : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={addMagnet.isPending || addFile.isPending}
          isDisabled={!file && !magnetLink.trim()}
          onPress={handleAddPress}
        >
          {translate('AddTorrent')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

interface AddTorrentModalProps {
  isOpen: boolean;
  onModalClose: () => void;
}

function AddTorrentModal({ isOpen, onModalClose }: AddTorrentModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <AddTorrentModalContent onModalClose={onModalClose} />
    </Modal>
  );
}

export default AddTorrentModal;
