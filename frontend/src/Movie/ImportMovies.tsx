import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import useRootFolders from 'RootFolder/useRootFolders';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import Movie from './Movie';
import { useAddMovie, useLookupMovies } from './useMovies';
import styles from './Movie.module.css';

interface UnmappedMovieFolder {
  name: string;
  path: string;
  parsedTitle: string;
  parsedYear: number;
}

const NO_FOLDERS: UnmappedMovieFolder[] = [];

// The lookup result that fits the folder: the one from the folder's year when there is one.
function pickMatch(movies: readonly Movie[], year: number) {
  return movies.find((movie) => year > 0 && movie.year === year) ?? movies[0];
}

interface ImportRowProps {
  folder: UnmappedMovieFolder;
  qualityProfileId: number;
}

function ImportRow({ folder, qualityProfileId }: ImportRowProps) {
  const term = folder.parsedYear
    ? `${folder.parsedTitle} ${folder.parsedYear}`
    : folder.parsedTitle;
  const { data, isFetching } = useLookupMovies(term);
  const { isAdding, addError, addMovie } = useAddMovie();
  const [added, setAdded] = useState(false);

  const match = pickMatch(data, folder.parsedYear);

  const handleAddPress = useCallback(() => {
    if (!match) {
      return;
    }

    addMovie(
      {
        tmdbId: match.tmdbId,
        title: match.title,
        year: match.year,
        qualityProfileId,
        path: folder.path,
        monitored: true,
        minimumAvailability: 'released',
        addOptions: { searchForMovie: false },
      },
      { onSuccess: () => setAdded(true) }
    );
  }, [match, folder.path, qualityProfileId, addMovie]);

  let matchCell: React.ReactNode = translate('NoResultsFound');

  if (isFetching) {
    matchCell = <LoadingIndicator size={20} />;
  } else if (match) {
    matchCell = (
      <>
        {match.title} ({match.year})
      </>
    );
  }

  return (
    <tr>
      <td>{folder.name}</td>
      <td>{matchCell}</td>
      <td>
        {added ? (
          translate('MovieAlreadyAdded')
        ) : (
          <Button
            kind={kinds.SUCCESS}
            isDisabled={isAdding || !match || !qualityProfileId}
            onPress={handleAddPress}
          >
            {translate('Import')}
          </Button>
        )}
        {addError ? <div>{getErrorMessage(addError)}</div> : null}
      </td>
    </tr>
  );
}

function ImportMovies() {
  const { data: rootFolders } = useRootFolders();
  const profiles = useQualityProfilesData();
  const [rootFolderPath, setRootFolderPath] = useState('');
  const [qualityProfileId, setQualityProfileId] = useState(0);

  const path = rootFolderPath || rootFolders[0]?.path || '';
  const profile = qualityProfileId || profiles[0]?.id || 0;

  const {
    data = NO_FOLDERS,
    isFetching,
    error,
  } = useApiQuery<UnmappedMovieFolder[]>({
    path: '/movie/import',
    queryParams: { path },
    queryOptions: { enabled: !!path, refetchOnWindowFocus: false },
  });

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

  return (
    <PageContent title={translate('ImportMovies')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Movies')}
          title={translate('ImportMovies')}
        />

        <div className={styles.actions}>
          <select
            className={styles.select}
            aria-label={translate('RootFolder')}
            value={path}
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
        </div>

        {isFetching ? <LoadingIndicator /> : null}

        {!isFetching && error ? <p>{getErrorMessage(error)}</p> : null}

        {!isFetching && !error && path && !data.length ? (
          <p>
            {translate('NoUnmappedMovieFolders')}{' '}
            <Link to="/add/movie">{translate('AddMovie')}</Link>
          </p>
        ) : null}

        {!isFetching && data.length ? (
          <table className={styles.table}>
            <thead>
              <tr>
                <th>{translate('Folder')}</th>
                <th>{translate('Movie')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {data.map((folder) => (
                <ImportRow
                  key={folder.path}
                  folder={folder}
                  qualityProfileId={profile}
                />
              ))}
            </tbody>
          </table>
        ) : null}
      </PageContentBody>
    </PageContent>
  );
}

export default ImportMovies;
