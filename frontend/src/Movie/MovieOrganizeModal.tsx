import React, { useCallback } from 'react';
import { SelectProvider, useSelect } from 'App/Select/SelectContext';
import CommandNames from 'Commands/CommandNames';
import { useExecuteCommand } from 'Commands/useCommands';
import Alert from 'Components/Alert';
import CheckInput from 'Components/Form/CheckInput';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import InlineMarkdown from 'Components/Markdown/InlineMarkdown';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import styles from 'Organize/OrganizePreviewModalContent.module.css';
import OrganizePreviewRow from 'Organize/OrganizePreviewRow';
import { useNamingSettings } from 'Settings/MediaManagement/Naming/useNamingSettings';
import { CheckInputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import useMovieRenamePreview, {
  MovieRenamePreview,
} from './useMovieRenamePreview';

interface MovieOrganizeContentProps {
  movieId: number;
  moviePath: string;
  onModalClose: () => void;
}

function MovieOrganizeContentInner({
  movieId,
  moviePath,
  onModalClose,
}: MovieOrganizeContentProps) {
  const executeCommand = useExecuteCommand();
  const {
    items,
    isFetching: isPreviewFetching,
    isFetched: isPreviewFetched,
    error: previewError,
  } = useMovieRenamePreview(movieId);
  const {
    isFetching: isNamingFetching,
    isFetched: isNamingFetched,
    error: namingError,
    data: naming,
  } = useNamingSettings();
  const { allSelected, allUnselected, getSelectedIds, selectAll, unselectAll } =
    useSelect<MovieRenamePreview>();

  const isFetching = isPreviewFetching || isNamingFetching;
  const isPopulated = isPreviewFetched && isNamingFetched;
  const error = previewError || namingError;
  const { renameMovies, standardMovieFormat } = naming;

  let selectAllValue: boolean | null = null;

  if (allSelected) {
    selectAllValue = true;
  } else if (allUnselected) {
    selectAllValue = false;
  }

  const handleSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      if (value) {
        selectAll();
      } else {
        unselectAll();
      }
    },
    [selectAll, unselectAll]
  );

  const handleOrganizePress = useCallback(() => {
    executeCommand({
      name: CommandNames.RenameMovieFiles,
      movieId,
      files: getSelectedIds(),
    });

    onModalClose();
  }, [movieId, getSelectedIds, executeCommand, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('OrganizeModalHeader')}</ModalHeader>

      <ModalBody>
        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <Alert kind={kinds.DANGER}>{translate('OrganizeLoadError')}</Alert>
        ) : null}

        {!isFetching && isPopulated && !items.length ? (
          <div>
            {renameMovies
              ? translate('OrganizeNothingToRename')
              : translate('OrganizeRenamingDisabled')}
          </div>
        ) : null}

        {!isFetching && isPopulated && items.length ? (
          <div>
            <Alert>
              <div>
                <InlineMarkdown
                  data={translate('OrganizeRelativePaths', {
                    path: moviePath,
                  })}
                  blockClassName={styles.path}
                />
              </div>

              <div>
                <InlineMarkdown
                  data={translate('OrganizeNamingPattern', {
                    episodeFormat: standardMovieFormat,
                  })}
                  blockClassName={styles.episodeFormat}
                />
              </div>
            </Alert>

            <div className={styles.previews}>
              {items.map((item) => (
                <OrganizePreviewRow
                  key={item.movieFileId}
                  id={item.movieFileId}
                  existingPath={item.existingPath}
                  newPath={item.newPath}
                />
              ))}
            </div>
          </div>
        ) : null}
      </ModalBody>

      <ModalFooter>
        {isPopulated && items.length ? (
          <CheckInput
            className={styles.selectAllInput}
            containerClassName={styles.selectAllInputContainer}
            name="selectAll"
            ariaLabel={translate('SelectAll')}
            value={selectAllValue}
            onChange={handleSelectAllChange}
          />
        ) : null}

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <Button kind={kinds.PRIMARY} onPress={handleOrganizePress}>
          {translate('Organize')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

function MovieOrganizeContent(props: MovieOrganizeContentProps) {
  const { items } = useMovieRenamePreview(props.movieId);

  return (
    <SelectProvider<MovieRenamePreview> items={items}>
      <MovieOrganizeContentInner {...props} />
    </SelectProvider>
  );
}

interface MovieOrganizeModalProps extends MovieOrganizeContentProps {
  isOpen: boolean;
}

function MovieOrganizeModal({
  isOpen,
  ...otherProps
}: MovieOrganizeModalProps) {
  return (
    <Modal
      isOpen={isOpen}
      size={sizes.EXTRA_EXTRA_LARGE}
      onModalClose={otherProps.onModalClose}
    >
      {isOpen ? <MovieOrganizeContent {...otherProps} /> : null}
    </Modal>
  );
}

export default MovieOrganizeModal;
