import React, { useCallback, useState } from 'react';
import addStyles from 'AddSeries/AddNewSeries/AddNewSeries.module.css';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import useDebounce from 'Helpers/Hooks/useDebounce';
import useQueryParams from 'Helpers/Hooks/useQueryParams';
import { icons, kinds } from 'Helpers/Props';
import useRootFolders from 'RootFolder/useRootFolders';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { InputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import Movie from './Movie';
import MoviePoster from './MoviePoster';
import { useAddMovie, useLookupMovies } from './useMovies';
import styles from './Movie.module.css';

interface SearchResultProps {
  movie: Movie;
}

function SearchResult({ movie }: SearchResultProps) {
  const { data: rootFolders } = useRootFolders();
  const profiles = useQualityProfilesData();
  const { isAdding, addError, addMovie } = useAddMovie();
  const [rootFolderPath, setRootFolderPath] = useState('');
  const [qualityProfileId, setQualityProfileId] = useState(0);
  const [monitored, setMonitored] = useState(true);
  const [searchForMovie, setSearchForMovie] = useState(true);
  const [added, setAdded] = useState(false);

  const isInLibrary = movie.id > 0;
  const folder = rootFolderPath || rootFolders[0]?.path || '';
  const profile = qualityProfileId || profiles[0]?.id || 0;

  const handleRootFolderChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setRootFolderPath(event.target.value);
    },
    []
  );

  const handleProfileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setQualityProfileId(Number(event.target.value));
    },
    []
  );

  const handleMonitoredChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMonitored(event.target.checked);
    },
    []
  );

  const handleSearchForMovieChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setSearchForMovie(event.target.checked);
    },
    []
  );

  const handleAdded = useCallback(() => {
    setAdded(true);
  }, []);

  const handleAddPress = useCallback(() => {
    addMovie(
      {
        tmdbId: movie.tmdbId,
        title: movie.title,
        year: movie.year,
        qualityProfileId: profile,
        rootFolderPath: folder,
        monitored,
        minimumAvailability: 'released',
        addOptions: { searchForMovie },
      },
      { onSuccess: handleAdded }
    );
  }, [
    addMovie,
    movie,
    profile,
    folder,
    monitored,
    searchForMovie,
    handleAdded,
  ]);

  return (
    <div className={styles.result}>
      <MoviePoster movie={movie} />

      <div className={styles.resultBody}>
        <div className={styles.resultTitle}>
          {movie.title} <span>({movie.year})</span>
        </div>

        {movie.overview ? (
          <div className={styles.overview}>{movie.overview}</div>
        ) : null}

        {isInLibrary || added ? (
          <div className={styles.actions}>
            <Icon name={icons.CHECK} /> {translate('MovieAlreadyAdded')}
          </div>
        ) : (
          <div className={styles.actions}>
            <select
              className={styles.select}
              aria-label={translate('RootFolder')}
              value={folder}
              onChange={handleRootFolderChange}
            >
              {rootFolders.map((rootFolder) => (
                <option key={rootFolder.id} value={rootFolder.path}>
                  {rootFolder.path}
                </option>
              ))}
            </select>

            <select
              className={styles.select}
              aria-label={translate('QualityProfile')}
              value={profile}
              onChange={handleProfileChange}
            >
              {profiles.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>

            <label>
              <input
                type="checkbox"
                checked={monitored}
                onChange={handleMonitoredChange}
              />{' '}
              {translate('Monitored')}
            </label>

            <label>
              <input
                type="checkbox"
                checked={searchForMovie}
                onChange={handleSearchForMovieChange}
              />{' '}
              {translate('SearchForMovie')}
            </label>

            <Button
              kind={kinds.SUCCESS}
              isDisabled={isAdding || !folder || !profile}
              onPress={handleAddPress}
            >
              {translate('Add')}
            </Button>
          </div>
        )}

        {addError ? (
          <p className={addStyles.error}>{getErrorMessage(addError)}</p>
        ) : null}
      </div>
    </div>
  );
}

function AddNewMovie() {
  const { term: initialTerm = '' } = useQueryParams<{ term: string }>();
  const [term, setTerm] = useState(initialTerm);
  const query = useDebounce(term, term ? 400 : 0);
  const { isFetching, error, data } = useLookupMovies(query);

  const handleChange = useCallback(({ value }: InputChanged<string>) => {
    setTerm(value);
  }, []);

  return (
    <PageContent title={translate('AddNewMovie')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Media')}
          title={translate('AddNewMovie')}
          actions={<Link to="/movies">{translate('Movies')}</Link>}
        />

        <div className={addStyles.searchSticky}>
          <div className={addStyles.searchWrap}>
            <Icon
              className={addStyles.searchIcon}
              name={icons.SEARCH}
              size={18}
            />

            <TextInput
              className={addStyles.searchInput}
              name="movieLookup"
              value={term}
              placeholder="eg. The Matrix, tmdb:603, imdb:tt0133093"
              autoFocus={true}
              onChange={handleChange}
            />
          </div>
        </div>

        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <p className={addStyles.error}>{getErrorMessage(error)}</p>
        ) : null}

        {!isFetching && !error && data.length
          ? data.map((movie) => (
              <SearchResult key={movie.tmdbId} movie={movie} />
            ))
          : null}

        {!isFetching && !error && !data.length && query ? (
          <div className={addStyles.emptyState}>
            {translate('CouldNotFindMovies', { term: query })}
          </div>
        ) : null}

        {term ? null : (
          <div className={addStyles.emptyState}>
            <div className={addStyles.emptyTitle}>
              {translate('AddNewMovie')}
            </div>
            <div className={addStyles.emptyBody}>
              {translate('AddNewMovieHelpText')}
            </div>
          </div>
        )}
      </PageContentBody>
    </PageContent>
  );
}

export default AddNewMovie;
