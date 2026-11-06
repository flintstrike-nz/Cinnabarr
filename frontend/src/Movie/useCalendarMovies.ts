import moment from 'moment-timezone';
import { useCalendarRange } from 'Calendar/useCalendar';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';
import Movie from './Movie';

export interface CalendarMovieRelease {
  key: string;
  movieId: number;
  title: string;
  label: string;
  date: string;
}

const NO_RELEASES: CalendarMovieRelease[] = [];

// Movies with a cinema, digital or physical release inside the days the calendar shows.
const useCalendarMovies = () => {
  const { start, end } = useCalendarRange();

  const { data } = useApiQuery<Movie[]>({
    path: '/calendar/movie',
    queryParams: {
      start: moment(start).startOf('day').toISOString(),
      end: moment(end).add(1, 'day').toISOString(),
      includeUnmonitored: false,
    },
    queryOptions: { enabled: !!start && !!end },
  });

  if (!data) {
    return NO_RELEASES;
  }

  return data.flatMap((movie) =>
    [
      { label: translate('InCinemas'), date: movie.inCinemas },
      { label: translate('DigitalRelease'), date: movie.digitalRelease },
      { label: translate('PhysicalRelease'), date: movie.physicalRelease },
    ]
      .filter((release) => !!release.date)
      .map((release) => ({
        key: `${movie.id}-${release.label}`,
        movieId: movie.id,
        title: movie.title,
        label: release.label,
        date: release.date as string,
      }))
  );
};

export const useCalendarMoviesForDay = (date: string) => {
  const releases = useCalendarMovies();
  const day = moment(date);

  return releases.filter((release) => day.isSame(moment(release.date), 'day'));
};

export default useCalendarMovies;
