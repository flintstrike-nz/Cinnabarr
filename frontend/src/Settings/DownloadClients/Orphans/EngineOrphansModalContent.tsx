import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { inputTypes, kinds } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import { useApproveEngineOrphans, useEngineOrphans } from './useEngineOrphans';

const columns: Column[] = [
  {
    name: 'path',
    get label() {
      return translate('Path');
    },
    isVisible: true,
  },
  {
    name: 'size',
    get label() {
      return translate('Size');
    },
    isVisible: true,
  },
  {
    name: 'lastWrite',
    get label() {
      return translate('EngineOrphansLastWritten');
    },
    isVisible: true,
  },
];

function getErrorMessage(error: ApiError | null) {
  return error?.statusBody?.message ?? error?.message;
}

interface EngineOrphansModalContentProps {
  onModalClose: () => void;
}

function EngineOrphansModalContent({
  onModalClose,
}: EngineOrphansModalContentProps) {
  const { data, isFetching, error } = useEngineOrphans();
  const { mutate, isPending, error: deleteError } = useApproveEngineOrphans();
  const [deleteAutomatically, setDeleteAutomatically] = useState(true);

  const files = useMemo(() => data?.files ?? [], [data]);

  const handleDeleteAutomaticallyChange = useCallback(
    ({ value }: InputChanged<boolean>) => {
      setDeleteAutomatically(value);
    },
    []
  );

  const handleDeletePress = useCallback(() => {
    mutate(
      { paths: files.map((f) => f.path), deleteAutomatically },
      { onSuccess: onModalClose }
    );
  }, [files, deleteAutomatically, mutate, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('EngineOrphans')}</ModalHeader>

      <ModalBody>
        {isFetching && !data ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <Alert kind={kinds.DANGER}>{getErrorMessage(error)}</Alert>
        ) : null}

        {data && !error ? (
          <>
            <Alert kind={kinds.INFO}>
              {translate('EngineOrphansReviewHelpText', {
                graceDays: data.graceDays,
              })}
            </Alert>

            {files.length ? (
              <>
                <Table columns={columns}>
                  <TableBody>
                    {files.map((file) => (
                      <TableRow key={file.path}>
                        <TableRowCell>{file.path}</TableRowCell>
                        <TableRowCell>{formatBytes(file.size)}</TableRowCell>
                        <RelativeDateCell date={file.lastWrite} />
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>

                <Form>
                  <FormRow>
                    <FormLabel>
                      {translate('EngineOrphansDeleteAutomatically')}
                    </FormLabel>

                    <FormInput
                      type={inputTypes.CHECK}
                      name="deleteAutomatically"
                      value={deleteAutomatically}
                      helpText={translate(
                        'EngineOrphansDeleteAutomaticallyHelpText'
                      )}
                      onChange={handleDeleteAutomaticallyChange}
                    />
                  </FormRow>
                </Form>
              </>
            ) : (
              <div>{translate('EngineOrphansNone')}</div>
            )}
          </>
        ) : null}

        {deleteError ? (
          <Alert kind={kinds.DANGER}>{getErrorMessage(deleteError)}</Alert>
        ) : null}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Close')}</Button>

        {files.length ? (
          <SpinnerErrorButton
            kind={kinds.DANGER}
            error={deleteError}
            isSpinning={isPending}
            onPress={handleDeletePress}
          >
            {translate('EngineOrphansDelete', { count: files.length })}
          </SpinnerErrorButton>
        ) : null}
      </ModalFooter>
    </ModalContent>
  );
}

export default EngineOrphansModalContent;
