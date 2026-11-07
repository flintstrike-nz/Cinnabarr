import React from 'react';
import { Link } from 'react-router-dom';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import translate from 'Utilities/String/translate';
import Movie from './Movie';
import { useMovies } from './useMovies';
import styles from './Movie.module.css';

interface Credit {
  id: number;
  name: string;
  character?: string;
  job?: string;
  type: 'cast' | 'crew';
}

const NO_CREDITS: Credit[] = [];
const CAST_SHOWN = 12;

function getNames(credits: readonly Credit[], job: string) {
  return credits
    .filter((credit) => credit.type === 'crew' && credit.job === job)
    .map((credit) => credit.name)
    .join(', ');
}

// Director, writers and the main cast, and the other movies of the same collection that are in the library.
function MovieCredits({ movie }: { movie: Movie }) {
  const { data: credits = NO_CREDITS } = useApiQuery<Credit[]>({
    path: '/credit',
    queryParams: { movieId: movie.id },
  });
  const { data: movies } = useMovies();

  const directors = getNames(credits, 'Director');
  const writers = getNames(credits, 'Writer');
  const cast = credits.filter((credit) => credit.type === 'cast');
  const others = movie.collectionTmdbId
    ? movies.filter(
        (m) =>
          m.collectionTmdbId === movie.collectionTmdbId && m.id !== movie.id
      )
    : [];

  if (!directors && !writers && !cast.length && !movie.collectionTitle) {
    return null;
  }

  return (
    <div className={styles.facts}>
      {directors ? (
        <div>
          {translate('Director')}: {directors}
        </div>
      ) : null}

      {writers ? (
        <div>
          {translate('Writers')}: {writers}
        </div>
      ) : null}

      {cast.length ? (
        <div>
          {translate('Cast')}:{' '}
          {cast
            .slice(0, CAST_SHOWN)
            .map((credit) =>
              credit.character
                ? `${credit.name} (${credit.character})`
                : credit.name
            )
            .join(', ')}
        </div>
      ) : null}

      {movie.collectionTitle ? (
        <div>
          {translate('Collection')}: {movie.collectionTitle}
          {others.length ? ' · ' : ''}
          {others.map((m, index) => (
            <span key={m.id}>
              {index > 0 ? ', ' : ''}
              <Link to={`/movie/${m.id}`}>{m.title}</Link>
            </span>
          ))}
        </div>
      ) : null}
    </div>
  );
}

export default MovieCredits;
