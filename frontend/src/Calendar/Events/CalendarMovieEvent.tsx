import React from 'react';
import Link from 'Components/Link/Link';
import styles from './CalendarMovieEvent.module.css';

interface CalendarMovieEventProps {
  movieId: number;
  title: string;
  label: string;
}

function CalendarMovieEvent({
  movieId,
  title,
  label,
}: CalendarMovieEventProps) {
  return (
    <div className={styles.movie}>
      <Link to={`/movie/${movieId}`}>{title}</Link>
      <div className={styles.label}>{label}</div>
    </div>
  );
}

export default CalendarMovieEvent;
