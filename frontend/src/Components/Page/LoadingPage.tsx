import React from 'react';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import LoadingMessage from 'Components/Loading/LoadingMessage';
import styles from './LoadingPage.module.css';

const logo = `${window.Sonarr.urlBase}/Content/Images/logo.svg`;

function LoadingPage() {
  return (
    <div className={styles.page}>
      <img className={styles.logoFull} src={logo} />
      <LoadingMessage />
      <LoadingIndicator />
    </div>
  );
}

export default LoadingPage;
