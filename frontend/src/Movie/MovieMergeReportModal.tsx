import React from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import translate from 'Utilities/String/translate';
import {
  clearMovieMergeReports,
  useImportMoviesState,
} from './importMoviesStore';
import styles from './Movie.module.css';

const fileName = (path: string) => path.split(/[\\/]/).pop() ?? path;

// What each finished merge did: how many files moved into the movie, and why each of the others stayed.
function MovieMergeReportModal() {
  const reports = useImportMoviesState((state) => state.reports);
  const withLeftovers = reports.filter((r) => r.results.some((x) => !x.merged));

  return (
    <Modal
      isOpen={withLeftovers.length > 0}
      onModalClose={clearMovieMergeReports}
    >
      <ModalContent onModalClose={clearMovieMergeReports}>
        <ModalHeader>{translate('MergeReport')}</ModalHeader>

        <ModalBody>
          {withLeftovers.map((report, index) => {
            const left = report.results.filter((r) => !r.merged);

            return (
              <section key={index}>
                <h3>
                  {translate('MergeReportSummary', {
                    title: report.movieTitle,
                    merged: report.results.length - left.length,
                    left: left.length,
                  })}
                </h3>

                <table className={styles.table}>
                  <thead>
                    <tr>
                      <th>{translate('MergeReportLeftFile')}</th>
                      <th>{translate('MergeReportWhy')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {left.map((result) => (
                      <tr key={result.path}>
                        <td title={result.path}>{fileName(result.path)}</td>
                        <td>{result.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </section>
            );
          })}
        </ModalBody>

        <ModalFooter>
          <Button onPress={clearMovieMergeReports}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default MovieMergeReportModal;
