import moment from 'moment-timezone';
import {
  useCalendarFilterValues,
  useCalendarRange,
} from 'Calendar/useCalendar';
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
  const { includeUnmonitored, tags } = useCalendarFilterValues();

  const { data } = useApiQuery<Movie[]>({
    path: '/calendar/movie',
    queryParams: {
      start: moment(start).startOf('day').toISOString(),
      end: moment(end).add(1, 'day').toISOString(),
      includeUnmonitored,
    },
    queryOptions: { enabled: !!start && !!end },
  });

  if (!data) {
    return NO_RELEASES;
  }

  const filtered = tags?.length
    ? data.filter((movie) => movie.tags.some((tag) => tags.includes(tag)))
    : data;

  return filtered.flatMap((movie) =>
    [
      { label: translate('InCinemas'), date: movie.inCinemas },
      { label: translate('DigitalRelease'), date: movie.digitalRelease },
      { label: translate('PhysicalRelease'), date: movie.physicalRelease },
    ]
      // Only the releases that fall inside the days shown (a movie is found by any one of its dates).
      .filter(
        (release) =>
          !!release.date &&
          moment(release.date).isBetween(start, end, 'day', '[]')
      )
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
