import classNames from 'classnames';
import React, { useCallback } from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import styles from './QueueFilterTabs.module.css';

// The Downloads page's status tabs (roadmap §6.2), each one of the queue's predefined filters.
const TABS = [
  { key: 'all', label: 'All' },
  { key: 'downloading', label: 'QueueFilterDownloading' },
  { key: 'completed', label: 'QueueFilterSeeding' },
  { key: 'paused', label: 'QueueFilterPaused' },
  { key: 'unlinked', label: 'QueueFilterUnlinked' },
  { key: 'missingFromEngine', label: 'QueueFilterMissingFromEngine' },
];

interface QueueFilterTabsProps {
  selectedFilterKey: string | number;
  onFilterSelect: (key: string) => void;
}

interface TabProps {
  filterKey: string;
  label: string;
  isSelected: boolean;
  onFilterSelect: (key: string) => void;
}

function Tab({ filterKey, label, isSelected, onFilterSelect }: TabProps) {
  const handlePress = useCallback(() => {
    onFilterSelect(filterKey);
  }, [filterKey, onFilterSelect]);

  return (
    <Link
      className={classNames(styles.tab, isSelected && styles.selectedTab)}
      role="tab"
      aria-selected={isSelected}
      onPress={handlePress}
    >
      {translate(label)}
    </Link>
  );
}

function QueueFilterTabs({
  selectedFilterKey,
  onFilterSelect,
}: QueueFilterTabsProps) {
  return (
    <div className={styles.tabs} role="tablist">
      {TABS.map(({ key, label }) => (
        <Tab
          key={key}
          filterKey={key}
          label={label}
          isSelected={selectedFilterKey === key}
          onFilterSelect={onFilterSelect}
        />
      ))}
    </div>
  );
}

export default QueueFilterTabs;
