import { useQueryClient } from '@tanstack/react-query';
import React, { useCallback } from 'react';
import { useExecuteCommand } from 'Commands/useCommands';
import Button from 'Components/Link/Button';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import DoctorReview from 'Doctor/DoctorReview';
import DoctorSettingsPanel from 'Doctor/DoctorSettingsPanel';
import ReorganisePanel from 'Doctor/ReorganisePanel';
import translate from 'Utilities/String/translate';

// The Doctor: library checks to review, and the reorganise tool (roadmap §4.12).
function Doctor() {
  const queryClient = useQueryClient();
  const executeCommand = useExecuteCommand();

  const handleRunPress = useCallback(() => {
    executeCommand({ name: 'Doctor' }, () => {
      queryClient.invalidateQueries({ queryKey: ['/doctor'] });
    });
  }, [executeCommand, queryClient]);

  return (
    <PageContent title={translate('Doctor')}>
      <PageContentBody>
        <PageHeading title={translate('Doctor')} />

        <p>{translate('DoctorCaption')}</p>

        <Button onPress={handleRunPress}>{translate('DoctorRunNow')}</Button>

        <DoctorReview />

        <DoctorSettingsPanel />

        <ReorganisePanel />
      </PageContentBody>
    </PageContent>
  );
}

export default Doctor;
