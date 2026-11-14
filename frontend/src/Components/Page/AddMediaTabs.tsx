import React from 'react';
import { useLocation } from 'react-router';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './ImportMediaTabs.module.css';

// Adding a show and adding a movie are two pages; these tabs switch between them.
function AddMediaTabs() {
  const { pathname } = useLocation();
  const isMovies = pathname.startsWith('/add/movie');

  return (
    <div className={styles.tabs}>
      <Button kind={isMovies ? kinds.DEFAULT : kinds.PRIMARY} to="/add/new">
        {translate('Series')}
      </Button>

      <Button kind={isMovies ? kinds.PRIMARY : kinds.DEFAULT} to="/add/movie">
        {translate('Movies')}
      </Button>
    </div>
  );
}

export default AddMediaTabs;
