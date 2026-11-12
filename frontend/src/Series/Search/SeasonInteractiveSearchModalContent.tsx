import React, { useMemo, useRef, useState } from 'react';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { scrollDirections } from 'Helpers/Props';
import InteractiveSearch from 'InteractiveSearch/InteractiveSearch';
import InteractiveSearchFilters from 'InteractiveSearch/InteractiveSearchFilters';
import { useClearReleasesOnUnmount } from 'InteractiveSearch/useReleases';
import formatSeason from 'Season/formatSeason';
import translate from 'Utilities/String/translate';
import styles from './SeasonInteractiveSearchModalContent.module.css';

export interface SeasonInteractiveSearchModalContentProps {
  episodeCount: number;
  seriesId: number;
  seasonNumber: number;
  versionSlotId?: number;
  onModalClose(): void;
}

function SeasonInteractiveSearchModalContent({
  episodeCount,
  seriesId,
  seasonNumber,
  versionSlotId,
  onModalClose,
}: SeasonInteractiveSearchModalContentProps) {
  const searchPayload = useMemo(
    () =>
      versionSlotId
        ? { seriesId, seasonNumber, versionSlotId }
        : { seriesId, seasonNumber },
    [seriesId, seasonNumber, versionSlotId]
  );

  useClearReleasesOnUnmount(searchPayload);
  const modalBodyRef = useRef<HTMLDivElement>(null);
  const [filter, setFilter] = useState('');

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {seasonNumber === null
          ? translate('InteractiveSearchModalHeader')
          : translate('InteractiveSearchModalHeaderSeason', {
              season: formatSeason(seasonNumber) as string,
            })}
      </ModalHeader>

      <ModalBody ref={modalBodyRef} scrollDirection={scrollDirections.BOTH}>
        <div className={styles.filterRow}>
          <InteractiveSearchFilters
            searchPayload={searchPayload}
            filter={filter}
            onFilterChange={setFilter}
          />
        </div>

        <InteractiveSearch
          searchPayload={searchPayload}
          scrollerRef={modalBodyRef}
          filter={filter}
        />
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <div>
          {translate('EpisodesInSeason', {
            episodeCount,
          })}
        </div>

        <Button onPress={onModalClose}>{translate('Close')}</Button>
      </ModalFooter>
    </ModalContent>
  );
}

export default SeasonInteractiveSearchModalContent;
