import React from 'react';
import Movie from './Movie';
import styles from './Movie.module.css';

interface MoviePosterProps {
  movie: Pick<Movie, 'title' | 'images'>;
  small?: boolean;
}

function MoviePoster({ movie, small = false }: MoviePosterProps) {
  const poster = movie.images.find((image) => image.coverType === 'poster');
  const url = poster?.url ?? poster?.remoteUrl;

  if (!url) {
    return <div className={small ? styles.smallPoster : styles.poster} />;
  }

  return (
    <img
      className={small ? styles.smallPoster : styles.poster}
      src={url}
      alt={movie.title}
      loading="lazy"
    />
  );
}

export default MoviePoster;
