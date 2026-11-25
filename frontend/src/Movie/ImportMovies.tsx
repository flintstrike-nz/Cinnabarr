import React, { useCallback, useEffect, useMemo, useState } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ImportMediaTabs from 'Components/Page/ImportMediaTabs';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import useApiQuery from 'Helpers/Hooks/useApiQuery';
import { kinds } from 'Helpers/Props';
import { useLibraryRootFolders } from 'RootFolder/useRootFolders';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import { CheckInputChanged } from 'typings/inputs';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import ImportMoviesCard from './ImportMoviesCard';
import {
  clearImportMovies,
  getImportMovieEligibility,
  setImportMoviesSelected,
  UnmappedMovieFolder,
  useImportMoviesState,
} from './importMoviesStore';
import Movie from './Movie';
import MovieMergeReportModal from './MovieMergeReportModal';
import { useImportMovieActions } from './useImportMovieActions';
import { useMovies } from './useMovies';
import styles from './ImportMovies.module.css';

const NO_FOLDERS: UnmappedMovieFolder[] = [];

// Library import for movies: the folders and loose video files of a root folder that the library doesn't have yet,
// each matched to a movie. New ones are added (a loose file is moved into a folder of its own); ones the library
// already has are merged into that movie.
function ImportMovies() {
  const { data: rootFolders } = useLibraryRootFolders('movies');
  const profiles = useQualityProfilesData();
  const { data: libraryMovies } = useMovies();
  const [rootFolderPath, setRootFolderPath] = useState('');
  const [qualityProfileId, setQualityProfileId] = useState(0);

  const path = rootFolderPath || rootFolders[0]?.path || '';
  const profile = qualityProfileId || profiles[0]?.id || 0;

  const {
    data = NO_FOLDERS,
    isFetching,
    isFetched,
    error,
  } = useApiQuery<UnmappedMovieFolder[]>({
    path: '/movie/import',
    queryParams: { path },
    queryOptions: { enabled: !!path, refetchOnWindowFocus: false },
  });

  const matches = useImportMoviesState((state) => state.matches);
  const selected = useImportMoviesState((state) => state.selected);
  const statuses = useImportMoviesState((state) => state.statuses);
  const { importNew, merge } = useImportMovieActions();

  // Rows imported since the page opened stay listed (marked done) until the root folder changes.
  const [folders, setFolders] = useState<UnmappedMovieFolder[]>([]);

  useEffect(() => {
    setFolders((current) => {
      const done = current.filter(
        (f) =>
          statuses[f.path]?.state === 'done' &&
          !data.some((d) => d.path === f.path)
      );

      return [...data, ...done].sort((a, b) =>
        a.name.localeCompare(b.name, undefined, { sensitivity: 'base' })
      );
    });
    // Only a new listing from the server rebuilds the rows.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data]);

  useEffect(() => {
    clearImportMovies();
    setFolders([]);
  }, [path]);

  useEffect(() => () => clearImportMovies(), []);

  const libraryByTmdbId = useMemo(
    () => new Map(libraryMovies.map((movie) => [movie.tmdbId, movie])),
    [libraryMovies]
  );

  const eligibility = useMemo(
    () =>
      getImportMovieEligibility(
        folders,
        matches,
        selected,
        new Set(libraryByTmdbId.keys())
      ),
    [folders, matches, selected, libraryByTmdbId]
  );

  const pending = folders.filter((f) => statuses[f.path]?.state !== 'done');
  const ready = pending.filter((f) => eligibility.get(f.path) === 'ready');
  const existing = pending.filter(
    (f) => eligibility.get(f.path) === 'existing'
  );
  const attention = pending.length - ready.length - existing.length;
  const toImport = ready.filter((f) => selected[f.path]);
  const isWorking = folders.some((f) => statuses[f.path]?.state === 'working');

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

  const handleSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      setImportMoviesSelected(
        ready.map((f) => f.path),
        value
      );
    },
    [ready]
  );

  const handleMerge = useCallback(
    (folder: UnmappedMovieFolder, movie: Movie) => {
      merge(movie, [folder.path]);
    },
    [merge]
  );

  const handleImportPress = useCallback(() => {
    toImport.forEach((folder) => {
      const match = matches[folder.path];

      if (match) {
        importNew(folder, match, {
          rootFolderPath: path,
          qualityProfileId: profile,
        });
      }
    });
  }, [toImport, matches, importNew, path, profile]);

  // Everything matched to a movie the library has goes into that movie, one merge per movie.
  const handleMergeAllPress = useCallback(() => {
    const byMovie = new Map<number, { movie: Movie; paths: string[] }>();

    existing.forEach((folder) => {
      const movie = libraryByTmdbId.get(matches[folder.path]?.tmdbId ?? 0);

      if (movie) {
        const entry = byMovie.get(movie.id) ?? { movie, paths: [] };
        entry.paths.push(folder.path);
        byMovie.set(movie.id, entry);
      }
    });

    byMovie.forEach(({ movie, paths }) => merge(movie, paths));
  }, [existing, libraryByTmdbId, matches, merge]);

  return (
    <PageContent title={translate('ImportMovies')}>
      <PageContentBody>
        <PageHeading
          scope={translate('Movies')}
          title={translate('ImportMovies')}
        />

        <ImportMediaTabs />

        <div className={styles.defaults}>
          <label className={styles.field}>
            <span className={styles.label}>{translate('RootFolder')}</span>
            <select
              className={styles.select}
              value={path}
              onChange={handleRootFolderChange}
            >
              {rootFolders.map((rootFolder) => (
                <option key={rootFolder.id} value={rootFolder.path}>
                  {rootFolder.path}
                </option>
              ))}
            </select>
          </label>

          <label className={styles.field}>
            <span className={styles.label}>{translate('QualityProfile')}</span>
            <select
              className={styles.select}
              value={profile}
              onChange={handleProfileChange}
            >
              {profiles.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>
        </div>

        {isFetching && !folders.length ? <LoadingIndicator /> : null}

        {!isFetching && error ? <p>{getErrorMessage(error)}</p> : null}

        {isFetched && !error && path && !folders.length ? (
          <p className={styles.empty}>
            {translate('NoUnmappedMovieFolders')}{' '}
            <Link to="/add/movie">{translate('AddMovie')}</Link>
          </p>
        ) : null}

        {folders.length ? (
          <>
            <div className={styles.list}>
              {folders.map((folder) => (
                <ImportMoviesCard
                  key={folder.path}
                  folder={folder}
                  eligibility={eligibility.get(folder.path) ?? 'searching'}
                  isSelected={!!selected[folder.path]}
                  status={statuses[folder.path]}
                  libraryMovie={libraryByTmdbId.get(
                    matches[folder.path]?.tmdbId ?? 0
                  )}
                  onMergePress={handleMerge}
                />
              ))}
            </div>

            <div className={styles.footer}>
              <CheckInput
                name="selectAll"
                ariaLabel={translate('SelectAll')}
                value={ready.length > 0 && toImport.length === ready.length}
                isDisabled={!ready.length}
                onChange={handleSelectAllChange}
              />

              <span className={styles.counts}>
                {translate('ImportMoviesCounts', {
                  ready: ready.length,
                  existing: existing.length,
                  attention,
                })}
              </span>

              <div className={styles.buttons}>
                {existing.length ? (
                  <Button isDisabled={isWorking} onPress={handleMergeAllPress}>
                    {translate('MergeIntoLibraryCount', {
                      count: existing.length,
                    })}
                  </Button>
                ) : null}

                <Button
                  kind={kinds.PRIMARY}
                  isDisabled={!toImport.length || !profile}
                  onPress={handleImportPress}
                >
                  {translate('ImportCountMovies', { count: toImport.length })}
                </Button>
              </div>
            </div>
          </>
        ) : null}

        <MovieMergeReportModal />
      </PageContentBody>
    </PageContent>
  );
}

export default ImportMovies;
