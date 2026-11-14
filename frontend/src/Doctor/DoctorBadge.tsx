import React, { ReactNode } from 'react';
import { useDoctorFlags } from './useDoctor';
import styles from './DoctorBadge.module.css';

interface DoctorBadgeProps {
  children: ReactNode;
}

// Puts a count over a button when Doctor has flagged files waiting for review.
function DoctorBadge({ children }: DoctorBadgeProps) {
  const { data } = useDoctorFlags();

  return (
    <span className={styles.wrapper}>
      {children}
      {data.length ? <span className={styles.badge}>{data.length}</span> : null}
    </span>
  );
}

export default DoctorBadge;
