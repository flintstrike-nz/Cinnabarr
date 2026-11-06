import React, { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { icons, kinds } from 'Helpers/Props';
import styles from 'Movie/Movie.module.css';
import { InputChanged } from 'typings/inputs';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import { SearchResult, useGrabSearchResult, useSearch } from './useSearch';

interface ResultRowProps {
  item: SearchResult;
  isGrabbing: boolean;
  onGrab: (payload: { guid: string; indexerId: number }) => void;
}

function getMatchLink(item: SearchResult) {
  if (item.mediaType === 'series') {
    return `/series/${item.mappedSeriesId}`;
  }

  return `/movie/${item.mappedMovieId}`;
}

function ResultRow({ item, isGrabbing, onGrab }: ResultRowProps) {
  const { release, decision } = item;

  const handleGrabPress = useCallback(() => {
    onGrab({ guid: release.guid, indexerId: release.indexerId });
  }, [release, onGrab]);

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
          translate('Unknown')
        ) : (
          <Link to={getMatchLink(item)}>{item.mappedTitle}</Link>
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
          isDisabled={isGrabbing || !item.downloadAllowed}
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
  const { data, isFetching, error } = useSearch(submitted);
  const { isGrabbing, grabError, grab } = useGrabSearchResult();

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
                <th>{translate('Title')}</th>
                <th>{translate('Library')}</th>
                <th>{translate('Indexer')}</th>
                <th>{translate('Size')}</th>
                <th>{translate('Seeders')}</th>
                <th>{translate('Age')}</th>
                <th>{translate('Quality')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {data.map((item) => (
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
