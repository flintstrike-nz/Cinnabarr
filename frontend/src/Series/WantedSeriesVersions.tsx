import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import styles from 'Movie/Movie.module.css';
import translate from 'Utilities/String/translate';
import SeriesTitleLink from './SeriesTitleLink';
import { useSingleSeries } from './useSeries';
import { useSearchSeriesVersion } from './useSeriesVersions';

type WantedKind = 'missing' | 'cutoff';

interface WantedSeriesVersionEpisode {
  episodeId: number;
  seasonNumber: number;
  episodeNumber: number;
  title?: string;
  airDateUtc?: string;
}

interface WantedSeriesVersion {
  seriesId: number;
  seriesTitle?: string;
  versionSlotId: number;
  versionLabel?: string;
  qualityProfileId: number;
  episodes: WantedSeriesVersionEpisode[];
}

const NO_VERSIONS: WantedSeriesVersion[] = [];

// The episodes the extra versions of series still want: no file for the version (missing), or a file below the
// version's profile cutoff.
const useWantedSeriesVersions = (kind: WantedKind) => {
  const result = useApiQuery<WantedSeriesVersion[]>({
    path: `/wanted/${kind}/versions`,
  });

  return { ...result, data: result.data ?? NO_VERSIONS };
};

interface RowProps {
  item: WantedSeriesVersion;
  isSearching: boolean;
  onSearch: (seriesId: number, versionSlotId: number) => void;
}

function WantedVersionRow({ item, isSearching, onSearch }: RowProps) {
  const series = useSingleSeries(item.seriesId);

  const handleSearchPress = useCallback(() => {
    onSearch(item.seriesId, item.versionSlotId);
  }, [item, onSearch]);

  return (
    <tr>
      <td>
        {series ? (
          <SeriesTitleLink
            seriesId={series.id}
            titleSlug={series.titleSlug}
            title={series.title}
          />
        ) : (
          item.seriesTitle
        )}
      </td>
      <td>{item.versionLabel}</td>
      <td>
        {item.episodes
          .map(
            (e) =>
              `S${String(e.seasonNumber).padStart(2, '0')}E${String(
                e.episodeNumber
              ).padStart(2, '0')}`
          )
          .join(', ')}
      </td>
      <td>
        <Button isDisabled={isSearching} onPress={handleSearchPress}>
          {translate('Search')}
        </Button>
      </td>
    </tr>
  );
}

// The wanted versions of series, shown under the episodes on the Wanted pages. Nothing is shown when no version wants anything.
function WantedSeriesVersions({ kind }: { kind: WantedKind }) {
  const { data } = useWantedSeriesVersions(kind);
  const { isSearching, searchVersion } = useSearchSeriesVersion();

  if (!data.length) {
    return null;
  }

  return (
    <section>
      <h3>{translate('SeriesVersions')}</h3>

      <table className={styles.table}>
        <tbody>
          {data.map((item) => (
            <WantedVersionRow
              key={`${item.seriesId}-${item.versionSlotId}`}
              item={item}
              isSearching={isSearching}
              onSearch={searchVersion}
            />
          ))}
        </tbody>
      </table>
    </section>
  );
}

export default WantedSeriesVersions;
