import React, { useCallback, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import PressLink from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { icons, kinds } from 'Helpers/Props';
import styles from 'Movie/Movie.module.css';
import { useVersionSlots } from 'Movie/useVersionSlots';
import { useSeriesVersions } from 'Series/useSeriesVersions';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import {
  GrabPayload,
  SearchResult,
  useGrabSearchResult,
  useSearch,
} from './useSearch';

interface ResultRowProps {
  item: SearchResult;
  isGrabbing: boolean;
  onGrab: (payload: GrabPayload) => void;
}

type SortKey =
  | 'title'
  | 'match'
  | 'indexer'
  | 'size'
  | 'seeders'
  | 'age'
  | 'quality';

function getSortValue(item: SearchResult, key: SortKey): string | number {
  switch (key) {
    case 'title':
      return item.release.title.toLowerCase();
    case 'match':
      return (item.mappedTitle ?? '').toLowerCase();
    case 'indexer':
      return item.release.indexer.toLowerCase();
    case 'size':
      return item.release.size;
    case 'seeders':
      return item.release.seeders ?? -1;
    case 'age':
      return item.release.age;
    default:
      return item.quality?.quality.name ?? '';
  }
}

// Where to add a title that matched nothing in the library, starting from the title parsed out of the release.
function getAddLink(item: SearchResult) {
  if (!item.parsedTitle || !item.guessedMediaType) {
    return null;
  }

  const term = encodeURIComponent(
    item.parsedYear
      ? `${item.parsedTitle} ${item.parsedYear}`
      : item.parsedTitle
  );

  return item.guessedMediaType === 'movie'
    ? `/add/movie?term=${term}`
    : `/add/new?term=${term}`;
}

function getSortArrow(
  sort: { key: SortKey; descending: boolean },
  sortKey: SortKey
) {
  if (sort.key !== sortKey) {
    return '';
  }

  return sort.descending ? ' ▼' : ' ▲';
}

interface SortHeaderProps {
  sortKey: SortKey;
  label: string;
  sort: { key: SortKey; descending: boolean };
  onSort: (key: SortKey) => void;
}

function SortHeader({ sortKey, label, sort, onSort }: SortHeaderProps) {
  const handlePress = useCallback(() => {
    onSort(sortKey);
  }, [sortKey, onSort]);

  return (
    <th>
      <PressLink onPress={handlePress}>
        {label}
        {getSortArrow(sort, sortKey)}
      </PressLink>
    </th>
  );
}

function getMatchLink(item: SearchResult) {
  if (item.mediaType === 'series') {
    return `/series/${item.mappedSeriesId}`;
  }

  return `/movie/${item.mappedMovieId}`;
}

const NEW_VERSION = -1;

// Exactly one of movieId and seriesId is set.
interface VersionPickerProps {
  movieId: number;
  seriesId: number;
  value: number;
  onChange: (value: number) => void;
  label: string;
  onLabelChange: (label: string) => void;
  profileId: number;
  onProfileChange: (profileId: number) => void;
}

// "Download another version": which version of the movie or series a release is for, or a new one.
function VersionPicker({
  movieId,
  seriesId,
  value,
  onChange,
  label,
  onLabelChange,
  profileId,
  onProfileChange,
}: VersionPickerProps) {
  const { data: movieSlots } = useVersionSlots(movieId);
  const { data: seriesVersions } = useSeriesVersions(seriesId);
  const slots = seriesId ? seriesVersions : movieSlots;
  const profiles = useQualityProfilesData();

  const handleChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      onChange(Number(event.target.value));
    },
    [onChange]
  );

  const handleLabelChange = useCallback(
    ({ value: text }: InputChanged<string>) => {
      onLabelChange(text);
    },
    [onLabelChange]
  );

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      onProfileChange(Number(event.target.value));
    },
    [onProfileChange]
  );

  return (
    <div>
      <select
        className={styles.select}
        aria-label={translate('Version')}
        value={value}
        onChange={handleChange}
      >
        {slots.map((slot) => (
          <option key={slot.id ?? 0} value={slot.id ?? 0}>
            {slot.isDefault ? translate('DefaultVersion') : slot.label}
          </option>
        ))}
        <option value={NEW_VERSION}>{translate('NewVersion')}</option>
      </select>

      {value === NEW_VERSION ? (
        <>
          <TextInput
            name="newVersionLabel"
            value={label}
            placeholder={translate('VersionLabelPlaceholder')}
            onChange={handleLabelChange}
          />

          <select
            className={styles.select}
            aria-label={translate('QualityProfile')}
            value={profileId || profiles[0]?.id || 0}
            onChange={handleProfileChange}
          >
            {profiles.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </>
      ) : null}
    </div>
  );
}

function ResultRow({ item, isGrabbing, onGrab }: ResultRowProps) {
  const { release, decision } = item;
  const profiles = useQualityProfilesData();
  const [version, setVersion] = useState(0);
  const [newLabel, setNewLabel] = useState('');
  const [newProfileId, setNewProfileId] = useState(0);
  const movieId = item.mediaType === 'movie' ? item.mappedMovieId ?? 0 : 0;
  const seriesId = item.mediaType === 'series' ? item.mappedSeriesId ?? 0 : 0;

  const handleGrabPress = useCallback(() => {
    const payload: GrabPayload = {
      guid: release.guid,
      indexerId: release.indexerId,
    };

    if (version === NEW_VERSION) {
      payload.newVersionLabel = newLabel;
      payload.newVersionQualityProfileId = newProfileId || profiles[0]?.id || 0;
    } else if (version !== 0) {
      payload.versionSlotId = version;
    }

    onGrab(payload);
  }, [release, onGrab, version, newLabel, newProfileId, profiles]);

  return (
    <tr>
      <td>
        {release.title}
        {decision.rejections.map((rejection) => (
          <div key={rejection.message} className={styles.rejection}>
            {rejection.message}
          </div>
        ))}
      </td>
      <td>
        {item.mediaType === 'unknown' ? (
          <>
            {translate('Unknown')}
            {getAddLink(item) ? (
              <div>
                <Link to={getAddLink(item) as string}>
                  {translate('Add')} {item.parsedTitle}
                </Link>
              </div>
            ) : null}
          </>
        ) : (
          <>
            <Link to={getMatchLink(item)}>{item.mappedTitle}</Link>
            {movieId || seriesId ? (
              <VersionPicker
                movieId={movieId}
                seriesId={seriesId}
                value={version}
                label={newLabel}
                profileId={newProfileId}
                onChange={setVersion}
                onLabelChange={setNewLabel}
                onProfileChange={setNewProfileId}
              />
            ) : null}
          </>
        )}
      </td>
      <td>{release.indexer}</td>
      <td>{formatBytes(release.size)}</td>
      <td>{release.seeders ?? ''}</td>
      <td>{Math.round(release.age)}d</td>
      <td>{item.quality?.quality.name}</td>
      <td>
        <Button
          kind={decision.approved ? kinds.SUCCESS : kinds.WARNING}
          isDisabled={
            isGrabbing ||
            !item.downloadAllowed ||
            (version === NEW_VERSION && !newLabel.trim())
          }
          onPress={handleGrabPress}
        >
          {translate('Grab')}
        </Button>
      </td>
    </tr>
  );
}

function SearchPage() {
  const [text, setText] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [indexerFilter, setIndexerFilter] = useState('');
  const [sort, setSort] = useState<{ key: SortKey; descending: boolean }>({
    key: 'seeders',
    descending: true,
  });
  const { data, isFetching, error } = useSearch(submitted);
  const { isGrabbing, grabError, grab } = useGrabSearchResult();

  const indexers = useMemo(
    () => [...new Set(data.map((item) => item.release.indexer))].sort(),
    [data]
  );

  const rows = useMemo(() => {
    const filtered = indexerFilter
      ? data.filter((item) => item.release.indexer === indexerFilter)
      : data;

    return [...filtered].sort((a, b) => {
      const left = getSortValue(a, sort.key);
      const right = getSortValue(b, sort.key);
      let order = 0;

      if (left < right) {
        order = -1;
      } else if (left > right) {
        order = 1;
      }

      return sort.descending ? -order : order;
    });
  }, [data, indexerFilter, sort]);

  const handleSort = useCallback((key: SortKey) => {
    setSort((current) => ({
      key,
      descending: current.key === key ? !current.descending : false,
    }));
  }, []);

  const handleIndexerChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setIndexerFilter(event.target.value);
    },
    []
  );

  const handleChange = useCallback(({ value }: InputChanged<string>) => {
    setText(value);
  }, []);

  const handleSubmit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault();
      setSubmitted(text.trim());
    },
    [text]
  );

  return (
    <PageContent title={translate('Search')}>
      <PageContentBody>
        <PageHeading title={translate('Search')} />

        <form onSubmit={handleSubmit}>
          <div className={styles.actions}>
            <TextInput
              name="releaseSearch"
              value={text}
              placeholder="eg. The Matrix 1999, Severance S02E01"
              autoFocus={true}
              onChange={handleChange}
            />

            <Button type="submit" kind={kinds.PRIMARY} isDisabled={isFetching}>
              <Icon name={icons.SEARCH} /> {translate('Search')}
            </Button>

            {indexers.length > 1 ? (
              <select
                className={styles.select}
                aria-label={translate('Indexer')}
                value={indexerFilter}
                onChange={handleIndexerChange}
              >
                <option value="">{translate('Indexers')}</option>
                {indexers.map((indexer) => (
                  <option key={indexer} value={indexer}>
                    {indexer}
                  </option>
                ))}
              </select>
            ) : null}
          </div>
        </form>

        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && error ? <p>{getErrorMessage(error)}</p> : null}

        {grabError ? <p>{getErrorMessage(grabError)}</p> : null}

        {!isFetching && !error && submitted && !data.length ? (
          <p>{translate('NoResultsFound')}</p>
        ) : null}

        {!isFetching && data.length ? (
          <table className={styles.table}>
            <thead>
              <tr>
                <SortHeader
                  sortKey="title"
                  label={translate('Title')}
                  sort={sort}
                  onSort={handleSort}
                />
                <SortHeader
                  sortKey="match"
                  label={translate('Library')}
                  sort={sort}
                  onSort={handleSort}
                />
                <SortHeader
                  sortKey="indexer"
                  label={translate('Indexer')}
                  sort={sort}
                  onSort={handleSort}
                />
                <SortHeader
                  sortKey="size"
                  label={translate('Size')}
                  sort={sort}
                  onSort={handleSort}
                />
                <SortHeader
                  sortKey="seeders"
                  label={translate('Seeders')}
                  sort={sort}
                  onSort={handleSort}
                />
                <SortHeader
                  sortKey="age"
                  label={translate('Age')}
                  sort={sort}
                  onSort={handleSort}
                />
                <SortHeader
                  sortKey="quality"
                  label={translate('Quality')}
                  sort={sort}
                  onSort={handleSort}
                />
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((item) => (
                <ResultRow
                  key={`${item.release.indexerId}-${item.release.guid}`}
                  item={item}
                  isGrabbing={isGrabbing}
                  onGrab={grab}
                />
              ))}
            </tbody>
          </table>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default SearchPage;
