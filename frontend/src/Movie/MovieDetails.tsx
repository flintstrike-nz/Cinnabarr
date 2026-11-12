import React, { useCallback, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { icons, kinds } from 'Helpers/Props';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import formatBytes from 'Utilities/Number/formatBytes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import MovieCredits from './MovieCredits';
import MovieEditPanel from './MovieEditPanel';
import MovieFiles from './MovieFiles';
import MovieHistory from './MovieHistory';
import MovieOrganizeModal from './MovieOrganizeModal';
import MoviePoster from './MoviePoster';
import {
  MovieRelease,
  useGrabMovieRelease,
  useMovieReleases,
} from './MovieRelease';
import MovieVersions from './MovieVersions';
import {
  useDeleteMovie,
  useMovie,
  useRefreshMovie,
  useSearchMovie,
  useUpdateMovie,
} from './useMovies';
import styles from './Movie.module.css';

interface InteractiveSearchProps {
  versionSlotId: number;
  movieId: number;
}

interface ReleaseRowProps {
  item: MovieRelease;
  isGrabbing: boolean;
  onGrab: (payload: { guid: string; indexerId: number }) => void;
}

function ReleaseRow({ item, isGrabbing, onGrab }: ReleaseRowProps) {
  const { release, decision, parsedInfo } = item;

  const handleGrabPress = useCallback(() => {
    if (release) {
      onGrab({ guid: release.guid, indexerId: release.indexerId });
    }
  }, [release, onGrab]);

  return (
    <tr>
      <td>
        {release?.title}
        {decision?.rejections.map((rejection) => (
          <div key={rejection.message} className={styles.rejection}>
            {rejection.message}
          </div>
        ))}
      </td>
      <td>{release?.indexer}</td>
      <td>{formatBytes(release?.size ?? 0)}</td>
      <td>{Math.round(release?.age ?? 0)}d</td>
      <td>{parsedInfo?.quality?.quality.name}</td>
      <td>
        <Button
          kind={decision?.approved ? kinds.SUCCESS : kinds.WARNING}
          isDisabled={isGrabbing || !release}
          onPress={handleGrabPress}
        >
          {translate('Grab')}
        </Button>
      </td>
    </tr>
  );
}

function InteractiveSearch({ movieId, versionSlotId }: InteractiveSearchProps) {
  const { data, isFetching, error } = useMovieReleases(
    movieId,
    true,
    versionSlotId
  );
  const { isGrabbing, grabError, grabRelease } = useGrabMovieRelease();

  if (isFetching) {
    return <LoadingIndicator />;
  }

  if (error) {
    return <p>{getErrorMessage(error)}</p>;
  }

  return (
    <>
      {grabError ? <p>{getErrorMessage(grabError)}</p> : null}

      <table className={styles.table}>
        <thead>
          <tr>
            <th>{translate('Title')}</th>
            <th>{translate('Indexer')}</th>
            <th>{translate('Size')}</th>
            <th>{translate('Age')}</th>
            <th>{translate('Quality')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {data.map((item) => (
            <ReleaseRow
              key={`${item.release?.indexerId}-${item.release?.guid}`}
              item={item}
              isGrabbing={isGrabbing}
              onGrab={grabRelease}
            />
          ))}
        </tbody>
      </table>

      {data.length ? null : <p>{translate('NoResultsFound')}</p>}
    </>
  );
}

function MovieDetails() {
  const { movieId } = useParams<{ movieId: string }>();
  const id = Number(movieId);
  const navigate = useNavigate();
  const { data: movie, isLoading } = useMovie(id);
  const profiles = useQualityProfilesData();
  const { updateMovie } = useUpdateMovie();
  const { searchMovie, isSearching } = useSearchMovie();
  const { deleteMovie, isDeleting } = useDeleteMovie(id);
  // The version an interactive search is open for (0 is the movie itself); null when it is closed.
  const [searchSlotId, setSearchSlotId] = useState<number | null>(null);
  const [isOrganizeOpen, setIsOrganizeOpen] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const { refreshMovie, isRefreshing } = useRefreshMovie();
  const [isDeleteFilesConfirmOpen, setIsDeleteFilesConfirmOpen] =
    useState(false);

  const handleMonitoredPress = useCallback(() => {
    if (movie) {
      updateMovie({ ...movie, monitored: !movie.monitored });
    }
  }, [movie, updateMovie]);

  const handleSearchPress = useCallback(() => {
    searchMovie(id);
  }, [searchMovie, id]);

  const handleInteractiveSearchPress = useCallback(() => {
    setSearchSlotId((value) => (value === null ? 0 : null));
  }, []);

  const handleVersionSearchPress = useCallback((slotId: number) => {
    setSearchSlotId(slotId);
  }, []);

  const handleEditToggle = useCallback(() => {
    setIsEditing((value) => !value);
  }, []);

  const handleRefreshPress = useCallback(() => {
    refreshMovie(id);
  }, [refreshMovie, id]);

  const handleOrganizeOpen = useCallback(() => {
    setIsOrganizeOpen(true);
  }, []);

  const handleOrganizeClose = useCallback(() => {
    setIsOrganizeOpen(false);
  }, []);

  const handleDeletePress = useCallback(() => {
    deleteMovie(
      { deleteFiles: false },
      { onSuccess: () => navigate('/movies') }
    );
  }, [deleteMovie, navigate]);

  const handleDeleteWithFilesPress = useCallback(() => {
    setIsDeleteFilesConfirmOpen(true);
  }, []);

  const handleDeleteWithFilesCancel = useCallback(() => {
    setIsDeleteFilesConfirmOpen(false);
  }, []);

  const handleDeleteWithFilesConfirm = useCallback(() => {
    setIsDeleteFilesConfirmOpen(false);
    deleteMovie(
      { deleteFiles: true },
      { onSuccess: () => navigate('/movies') }
    );
  }, [deleteMovie, navigate]);

  if (isLoading) {
    return <LoadingIndicator />;
  }

  if (!movie) {
    return <NotFound message={translate('MovieNotFound')} />;
  }

  return (
    <PageContent title={movie.title}>
      <PageContentBody>
        <PageHeading
          scope={translate('Movies')}
          title={`${movie.title} (${movie.year})`}
        />

        <div className={styles.header}>
          <MoviePoster movie={movie} />

          <div className={styles.resultBody}>
            <div className={styles.facts}>
              <span>{movie.runtime ? `${movie.runtime} min` : ''}</span>
              <span>{movie.certification}</span>
              <span>{movie.genres.join(', ')}</span>
              <span>
                {profiles.find((p) => p.id === movie.qualityProfileId)?.name}
              </span>
              <span>
                {movie.hasFile
                  ? formatBytes(movie.sizeOnDisk)
                  : translate('Missing')}
              </span>
            </div>

            <div className={styles.facts}>{movie.path}</div>

            <div className={styles.overview}>{movie.overview}</div>

            <MovieCredits movie={movie} />

            {isEditing ? (
              <MovieEditPanel movie={movie} onDone={handleEditToggle} />
            ) : null}

            <div className={styles.actions}>
              <Button onPress={handleMonitoredPress}>
                <Icon
                  name={movie.monitored ? icons.MONITORED : icons.UNMONITORED}
                />{' '}
                {movie.monitored
                  ? translate('Monitored')
                  : translate('Unmonitored')}
              </Button>

              <Button isDisabled={isSearching} onPress={handleSearchPress}>
                <Icon name={icons.SEARCH} /> {translate('SearchMovie')}
              </Button>

              <Button onPress={handleInteractiveSearchPress}>
                <Icon name={icons.INTERACTIVE} />{' '}
                {translate('InteractiveSearch')}
              </Button>

              <Button onPress={handleEditToggle}>
                <Icon name={icons.EDIT} /> {translate('Edit')}
              </Button>

              <Button isDisabled={isRefreshing} onPress={handleRefreshPress}>
                <Icon name={icons.REFRESH} /> {translate('Refresh')}
              </Button>

              <Button isDisabled={!movie.hasFile} onPress={handleOrganizeOpen}>
                <Icon name={icons.ORGANIZE} /> {translate('Organize')}
              </Button>

              <Button
                kind={kinds.DANGER}
                isDisabled={isDeleting}
                onPress={handleDeletePress}
              >
                <Icon name={icons.DELETE} /> {translate('DeleteMovie')}
              </Button>

              <Button
                kind={kinds.DANGER}
                isDisabled={isDeleting}
                onPress={handleDeleteWithFilesPress}
              >
                <Icon name={icons.DELETE} /> {translate('DeleteMovieAndFiles')}
              </Button>
            </div>
          </div>
        </div>

        {searchSlotId === null ? null : (
          <InteractiveSearch
            key={searchSlotId}
            movieId={movie.id}
            versionSlotId={searchSlotId}
          />
        )}

        <MovieVersions
          movieId={movie.id}
          onInteractiveSearch={handleVersionSearchPress}
        />

        <ConfirmModal
          isOpen={isDeleteFilesConfirmOpen}
          kind={kinds.DANGER}
          title={translate('DeleteMovieAndFiles')}
          message={translate('DeleteMovieAndFilesConfirmation')}
          confirmLabel={translate('Delete')}
          onConfirm={handleDeleteWithFilesConfirm}
          onCancel={handleDeleteWithFilesCancel}
        />

        <MovieFiles movieId={movie.id} />

        <MovieHistory movieId={movie.id} />

        <MovieOrganizeModal
          isOpen={isOrganizeOpen}
          movieId={movie.id}
          moviePath={movie.path}
          onModalClose={handleOrganizeClose}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default MovieDetails;
