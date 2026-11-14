import classNames from 'classnames';
import React from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import Movie from '../Movie';
import styles from './MovieIndexPoster.module.css';

interface MovieIndexPosterProps {
  movie: Movie;
  profileName?: string;
  isSelectMode: boolean;
  isSelected: boolean;
  onToggle: (event: React.ChangeEvent<HTMLInputElement>) => void;
}

function MovieIndexPoster({
  movie,
  profileName,
  isSelectMode,
  isSelected,
  onToggle,
}: MovieIndexPosterProps) {
  const poster = movie.images.find((image) => image.coverType === 'poster');
  const url = poster?.url ?? poster?.remoteUrl;

  return (
    <div className={styles.item}>
      <div className={styles.posterContainer}>
        {isSelectMode ? (
          <input
            className={styles.checkbox}
            type="checkbox"
            aria-label={translate('SelectRow')}
            data-id={movie.id}
            checked={isSelected}
            onChange={onToggle}
          />
        ) : null}

        <span className={styles.monitored}>
          <Icon
            name={movie.monitored ? icons.MONITORED : icons.UNMONITORED}
            size={12}
            title={
              movie.monitored
                ? translate('Monitored')
                : translate('Unmonitored')
            }
          />
        </span>

        <Link className={styles.link} to={`/movie/${movie.id}`}>
          {url ? (
            <img
              className={styles.poster}
              src={url}
              alt={movie.title}
              loading="lazy"
            />
          ) : (
            <div className={styles.noPoster}>{movie.title}</div>
          )}
        </Link>

        <div
          className={classNames(
            styles.bar,
            movie.hasFile && styles.hasFile,
            !movie.hasFile && !movie.monitored && styles.unmonitored
          )}
          title={movie.hasFile ? translate('Downloaded') : translate('Missing')}
        />
      </div>

      <div className={styles.title} title={movie.title}>
        {movie.title}
      </div>
      <div className={styles.info}>
        {[
          movie.year || null,
          profileName,
          movie.hasFile ? formatBytes(movie.sizeOnDisk) : null,
        ]
          .filter(Boolean)
          .join(' · ')}
      </div>
    </div>
  );
}

export default MovieIndexPoster;
