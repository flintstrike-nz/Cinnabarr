import React, { useCallback, useState } from 'react';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';
import styles from './UpdateBanner.module.css';

interface NewVersion {
  available: boolean;
  latest?: string;
  message?: string;
  url?: string;
}

const STORAGE_KEY = 'cinnabarr.dismissedUpdate';

const getDismissed = () => {
  try {
    return window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
};

// A line at the top when a newer build of Cinnabarr exists. It is shown again when an even newer one appears.
function UpdateBanner() {
  const { data } = useApiQuery<NewVersion>({
    path: '/update/available',
    queryOptions: { refetchInterval: 6 * 60 * 60 * 1000 },
  });
  const [dismissed, setDismissed] = useState(getDismissed);

  const handleDismiss = useCallback(() => {
    setDismissed(data?.latest ?? null);

    try {
      window.localStorage.setItem(STORAGE_KEY, data?.latest ?? '');
    } catch {
      // Remembering is a convenience only.
    }
  }, [data?.latest]);

  if (!data?.available || dismissed === data.latest) {
    return null;
  }

  return (
    <div className={styles.banner}>
      <span>
        {translate('UpdateBannerAvailable')}
        {data.message ? `: ${data.message}` : ''}
      </span>
      {data.url ? (
        <a href={data.url} target="_blank" rel="noreferrer">
          {translate('UpdateBannerDetails')}
        </a>
      ) : null}
      <span>{translate('UpdateBannerHow')}</span>
      <button
        className={styles.dismiss}
        type="button"
        aria-label={translate('Close')}
        onClick={handleDismiss}
      >
        ×
      </button>
    </div>
  );
}

export default UpdateBanner;
