import classNames from 'classnames';
import React from 'react';
import Link from 'Components/Link/Link';
import QueueModel from 'typings/Queue';
import formatBytes from 'Utilities/Number/formatBytes';
import styles from './QueueCards.module.css';

interface QueueCardsProps {
  records: QueueModel[];
}

// The downloads as cards for a phone: what it is, how far along, how long left.
function QueueCards({ records }: QueueCardsProps) {
  return (
    <div className={styles.cards}>
      {records.map((item) => {
        const percent = item.size
          ? Math.round(((item.size - item.sizeLeft) / item.size) * 100)
          : 0;
        let target: string | undefined = undefined;

        if (item.movieId) {
          target = `/movie/${item.movieId}`;
        } else if (item.seriesId) {
          target = `/series/${item.seriesId}`;
        }

        const title = item.movieTitle ?? item.title;

        return (
          <div key={item.id} className={styles.card}>
            <div className={styles.title} title={item.title}>
              {target ? <Link to={target}>{title}</Link> : title}
              {item.versionLabel ? ` · ${item.versionLabel}` : ''}
            </div>

            <div className={styles.track}>
              <div
                className={classNames(
                  styles.fill,
                  percent >= 100 && styles.done
                )}
                style={{ width: `${percent}%` }}
              />
            </div>

            <div className={styles.meta}>
              <span>
                {percent}% · {formatBytes(item.size)}
              </span>
              <span>
                {item.timeLeft && item.status === 'downloading'
                  ? item.timeLeft
                  : item.status}
              </span>
            </div>
          </div>
        );
      })}
    </div>
  );
}

export default QueueCards;
