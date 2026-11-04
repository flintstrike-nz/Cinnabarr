import React, { useMemo } from 'react';
import PageSectionContent from 'Components/Page/PageSectionContent';
import settingsCardStyles from 'Components/SettingsCard/SettingsCard.module.css';
import {
  getProviderTestStatus,
  ProviderTestAllResult,
} from 'Settings/ProviderTestAllResult';
import translate from 'Utilities/String/translate';
import DownloadClient from './DownloadClient';
import { useSortedDownloadClients } from './useDownloadClients';

interface DownloadClientsProps {
  testResults?: ProviderTestAllResult[];
}

// Embarr downloads only with its built-in engine (roadmap §4.4), so download clients can be edited but not added.
function DownloadClients({ testResults }: DownloadClientsProps) {
  const { isFetching, isFetched, data, error } = useSortedDownloadClients();

  const testResultById = useMemo(
    () => new Map(testResults?.map((result) => [result.id, result])),
    [testResults]
  );

  return (
    <PageSectionContent
      errorMessage={translate('DownloadClientsLoadError')}
      error={error}
      isFetching={isFetching}
      isPopulated={isFetched}
    >
      <div className={settingsCardStyles.grid}>
        {data.map((item) => {
          return (
            <DownloadClient
              key={item.id}
              {...item}
              testStatus={
                testResults
                  ? getProviderTestStatus(testResultById.get(item.id))
                  : undefined
              }
            />
          );
        })}
      </div>
    </PageSectionContent>
  );
}

export default DownloadClients;
