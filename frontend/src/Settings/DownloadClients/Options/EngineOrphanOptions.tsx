import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes } from 'Helpers/Props';
import EngineOrphansModal from 'Settings/DownloadClients/Orphans/EngineOrphansModal';
import {
  useEngineOrphanStatus,
  useStopEngineOrphanCleanup,
} from 'Settings/DownloadClients/Orphans/useEngineOrphans';
import { InputChanged } from 'typings/inputs';
import { PendingSection } from 'typings/pending';
import translate from 'Utilities/String/translate';
import { DownloadClientSettingsModel } from './useDownloadClientSettings';

interface EngineOrphanOptionsProps {
  settings: PendingSection<DownloadClientSettingsModel>;
  onInputChange: (change: InputChanged) => void;
}

// Leftover partial download files no torrent owns (roadmap §4.13).
function EngineOrphanOptions({
  settings,
  onInputChange,
}: EngineOrphanOptionsProps) {
  const [isReviewOpen, setIsReviewOpen] = useState(false);
  const { data: status, error: statusError } = useEngineOrphanStatus();
  const { mutate: stop, isPending: isStopping } = useStopEngineOrphanCleanup();

  const handleStopPress = useCallback(() => {
    stop();
  }, [stop]);

  const handleReviewPress = useCallback(() => {
    setIsReviewOpen(true);
  }, []);

  const handleReviewClose = useCallback(() => {
    setIsReviewOpen(false);
  }, []);

  return (
    <FieldSet
      legend={translate('EngineOrphans')}
      caption={translate('EngineOrphansCaption')}
    >
      <Form>
        <FormRow>
          <FormLabel>{translate('EngineOrphansGraceDays')}</FormLabel>

          <FormInputHelpText
            text={translate('EngineOrphansGraceDaysHelpText')}
          />

          <FormInput
            type={inputTypes.NUMBER}
            name="engineOrphanGraceDays"
            min={1}
            unit={translate('EngineSeedingUnitDays')}
            onChange={onInputChange}
            {...settings.engineOrphanGraceDays}
          />
        </FormRow>

        <FormRow>
          <FormLabel>{translate('EngineOrphansDeleteAutomatically')}</FormLabel>

          <div>
            {status ? (
              <FormInputHelpText
                text={translate(
                  status.deleteAutomatically
                    ? 'EngineOrphansDeleteAutomaticallyOnHelpText'
                    : 'EngineOrphansDeleteAutomaticallyOffHelpText'
                )}
              />
            ) : null}

            {!status && statusError ? (
              <FormInputHelpText
                text={translate('EngineOrphansStatusLoadError')}
                isError={true}
              />
            ) : null}

            {!status && !statusError ? <LoadingIndicator /> : null}

            {status?.deleteAutomatically ? (
              <SpinnerButton isSpinning={isStopping} onPress={handleStopPress}>
                {translate('EngineOrphansStopDeletingAutomatically')}
              </SpinnerButton>
            ) : null}
          </div>
        </FormRow>

        <FormRow>
          <FormLabel>{translate('EngineOrphansReview')}</FormLabel>

          <Button onPress={handleReviewPress}>
            {translate('EngineOrphansReview')}
          </Button>
        </FormRow>
      </Form>

      <EngineOrphansModal
        isOpen={isReviewOpen}
        onModalClose={handleReviewClose}
      />
    </FieldSet>
  );
}

export default EngineOrphanOptions;
