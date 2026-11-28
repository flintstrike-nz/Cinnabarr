import React from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import translate from 'Utilities/String/translate';
import { clearMergeReports, useMergeReports } from './useMergeIntoSeries';
import styles from './MergeReportModal.module.css';

const fileName = (path: string) => path.split(/[\\/]/).pop() ?? path;

// What each finished merge did: how many files moved into the series, and why each of the others stayed.
function MergeReportModal() {
  const reports = useMergeReports();

  return (
    <Modal isOpen={reports.length > 0} onModalClose={clearMergeReports}>
      <ModalContent onModalClose={clearMergeReports}>
        <ModalHeader>{translate('MergeReport')}</ModalHeader>

        <ModalBody>
          {reports.map((report, index) => {
            const merged = report.results.filter((r) => r.merged);
            const left = report.results.filter((r) => !r.merged);

            return (
              <section key={index} className={styles.report}>
                <h3 className={styles.title}>
                  {translate('MergeReportSummary', {
                    title: report.seriesTitle,
                    merged: merged.length,
                    left: left.length,
                  })}
                </h3>

                {left.length ? (
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
                          <td className={styles.path} title={result.path}>
                            {fileName(result.path)}
                          </td>
                          <td>{result.reason}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                ) : null}
              </section>
            );
          })}
        </ModalBody>

        <ModalFooter>
          <Button onPress={clearMergeReports}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default MergeReportModal;
