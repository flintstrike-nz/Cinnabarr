import moment from 'moment';
import React, { Fragment } from 'react';
import CalendarMovieEvent from 'Calendar/Events/CalendarMovieEvent';
import useCalendar from 'Calendar/useCalendar';
import useCalendarMovies from 'Movie/useCalendarMovies';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import AgendaEvent from './AgendaEvent';
import styles from './Agenda.module.css';

function Agenda() {
  const { data } = useCalendar();
  const movies = useCalendarMovies();
  const { longDateFormat } = useUiSettingsValues();

  // Every day that has an episode or a movie release, in order.
  const days = [
    ...new Set([
      ...data.map((item) => moment(item.airDateUtc).format('YYYY-MM-DD')),
      ...movies.map((movie) => moment(movie.date).format('YYYY-MM-DD')),
    ]),
  ].sort();

  return (
    <div className={styles.agenda}>
      {days.map((day) => (
        <Fragment key={day}>
          <div className={styles.dayHeader}>
            {moment(day).format(longDateFormat)}
          </div>

          {data
            .filter(
              (item) => moment(item.airDateUtc).format('YYYY-MM-DD') === day
            )
            .map((item) => (
              <AgendaEvent key={item.id} {...item} />
            ))}

          {movies
            .filter((movie) => moment(movie.date).format('YYYY-MM-DD') === day)
            .map((movie) => (
              <CalendarMovieEvent
                key={movie.key}
                movieId={movie.movieId}
                title={movie.title}
                label={movie.label}
              />
            ))}
        </Fragment>
      ))}
    </div>
  );
}

export default Agenda;
