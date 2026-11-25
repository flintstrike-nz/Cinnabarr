import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import translate from 'Utilities/String/translate';
import OperationsList from './OperationsList';
import {
  useClearFinishedOperations,
  useFileOperations,
} from './useFileOperations';

// The operations queue as a page of its own: what the panel shows, full screen (and the view used on a phone).
function OperationsPage() {
  const { operations, activeCount } = useFileOperations();
  const { mutate: clearFinished, isPending: isClearing } =
    useClearFinishedOperations();

  const handleClear = useCallback(() => clearFinished(), [clearFinished]);

  return (
    <PageContent title={translate('Operations')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Operations')}
          title={translate('Queue')}
          meta={
            activeCount
              ? translate('OperationsActive', { count: activeCount })
              : undefined
          }
          actions={
            operations.length > activeCount ? (
              <Button isDisabled={isClearing} onPress={handleClear}>
                {translate('OperationsClearFinished')}
              </Button>
            ) : undefined
          }
        />

        <OperationsList operations={operations} />
      </PageContentBody>
    </PageContent>
  );
}

export default OperationsPage;
