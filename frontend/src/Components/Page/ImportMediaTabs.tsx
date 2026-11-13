import React from 'react';
import { useLocation } from 'react-router';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './ImportMediaTabs.module.css';

// Library Import for TV shows and for movies are two pages; these tabs switch between them.
function ImportMediaTabs() {
  const { pathname } = useLocation();
  const isMovies = pathname.startsWith('/add/import/movie');

  return (
    <div className={styles.tabs}>
      <Button kind={isMovies ? kinds.DEFAULT : kinds.PRIMARY} to="/add/import">
        {translate('Series')}
      </Button>

      <Button
        kind={isMovies ? kinds.PRIMARY : kinds.DEFAULT}
        to="/add/import/movie"
      >
        {translate('Movies')}
      </Button>
    </div>
  );
}

export default ImportMediaTabs;
