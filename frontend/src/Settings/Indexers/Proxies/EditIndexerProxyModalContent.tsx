import React, { useCallback, useEffect } from 'react';
import Form from 'Components/Form/Form';
import FormInput from 'Components/Form/FormInput';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import FormRow from 'Components/Form/FormRow';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { inputTypes, kinds } from 'Helpers/Props';
import { useShowAdvancedSettings } from 'Settings/advancedSettingsStore';
import { SelectedSchema } from 'Settings/useProviderSchema';
import { EnhancedSelectInputChanged, InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import { useManageIndexerProxy } from './useIndexerProxies';

export interface EditIndexerProxyModalContentProps {
  id?: number;
  selectedSchema?: SelectedSchema;
  onModalClose: () => void;
  onDeletePress?: () => void;
}

function EditIndexerProxyModalContent({
  id,
  selectedSchema,
  onModalClose,
  onDeletePress,
}: EditIndexerProxyModalContentProps) {
  const showAdvancedSettings = useShowAdvancedSettings();

  const {
    item,
    updateFieldValue,
    updateValue,
    saveProvider,
    isSaving,
    saveError,
    testProvider,
    isTesting,
    validationErrors,
    validationWarnings,
  } = useManageIndexerProxy(id, selectedSchema);

  const wasSaving = usePrevious(isSaving);

  const { implementationName, name, fields, tags } = item;

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error - change is not yet typed
      updateValue(change.name, change.value);
    },
    [updateValue]
  );

  const handleFieldChange = useCallback(
    ({
      name,
      value,
      additionalProperties,
    }: EnhancedSelectInputChanged<unknown>) => {
      updateFieldValue({ [name]: value, ...additionalProperties });
    },
    [updateFieldValue]
  );

  const handleTestPress = useCallback(() => {
    testProvider();
  }, [testProvider]);

  const handleSavePress = useCallback(() => {
    saveProvider();
  }, [saveProvider]);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [isSaving, wasSaving, saveError, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader subtitle={id ? name.previousValue ?? name.value : undefined}>
        {id
          ? translate('EditIndexerProxyImplementation', { implementationName })
          : translate('AddIndexerProxyImplementation', { implementationName })}
      </ModalHeader>

      <ModalBody>
        <Form
          validationErrors={validationErrors}
          validationWarnings={validationWarnings}
        >
          <FormRow>
            <FormLabel>{translate('Name')}</FormLabel>

            <FormInput
              type={inputTypes.TEXT}
              name="name"
              {...name}
              onChange={handleInputChange}
            />
          </FormRow>

          <FormRow>
            <FormLabel>{translate('Tags')}</FormLabel>
            <FormInputHelpText text={translate('IndexerProxyTagsHelpText')} />
            <FormInput
              type={inputTypes.TAG}
              name="tags"
              {...tags}
              onChange={handleInputChange}
            />
          </FormRow>

          {fields.map((field) => (
            <ProviderFieldFormGroup
              key={field.name}
              {...field}
              advancedSettings={showAdvancedSettings}
              provider="indexerproxy"
              providerData={item}
              onChange={handleFieldChange}
            />
          ))}
        </Form>
      </ModalBody>

      <ModalFooter>
        {id ? (
          <Button kind={kinds.DANGER} onPress={onDeletePress}>
            {translate('Delete')}
          </Button>
        ) : null}

        <SpinnerErrorButton
          isSpinning={isTesting}
          error={saveError}
          onPress={handleTestPress}
        >
          {translate('Test')}
        </SpinnerErrorButton>

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          isSpinning={isSaving}
          error={saveError}
          onPress={handleSavePress}
        >
          {translate('Save')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default EditIndexerProxyModalContent;
