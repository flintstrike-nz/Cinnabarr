import classNames from 'classnames';
import React from 'react';
import { useLocation } from 'react-router-dom';
import useQueueStatus from 'Activity/Queue/Status/useQueueStatus';
import { toggleIsSidebarVisible, useAppDimension } from 'App/appStore';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './MobileTabBar.module.css';

// On a phone: the three things most used (see the library, add something, check downloads) one tap away.
function MobileTabBar() {
  const isSmallScreen = useAppDimension('isSmallScreen');
  const { pathname } = useLocation();
  const { count } = useQueueStatus();

  if (!isSmallScreen) {
    return null;
  }

  const isActive = (prefix: string) => pathname.startsWith(prefix);

  return (
    <nav className={styles.bar} aria-label={translate('Menu')}>
      <Link
        className={classNames(
          styles.tab,
          (isActive('/library') || isActive('/movie') || isActive('/series')) &&
            styles.active
        )}
        to="/library"
      >
        <Icon name={icons.SERIES_CONTINUING} size={20} />
        {translate('Library')}
      </Link>

      <Link
        className={classNames(styles.tab, isActive('/add') && styles.active)}
        to="/add/new"
      >
        <Icon name={icons.ADD} size={20} />
        {translate('Add')}
      </Link>

      <Link
        className={classNames(
          styles.tab,
          isActive('/downloads') && styles.active
        )}
        to="/downloads"
      >
        <Icon name={icons.DOWNLOADING} size={20} />
        {translate('Downloads')}
        {count ? <span className={styles.badge}>{count}</span> : null}
      </Link>

      <Link className={styles.tab} onPress={toggleIsSidebarVisible}>
        <Icon name={icons.NAVBAR_COLLAPSE} size={20} />
        {translate('Menu')}
      </Link>
    </nav>
  );
}

export default MobileTabBar;
