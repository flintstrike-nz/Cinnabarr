import React, { useEffect, useMemo, useRef } from 'react';
import { useParams } from 'react-router';
import {
  setAddSeriesOption,
  useAddSeriesOption,
} from 'AddSeries/addSeriesOptionsStore';
import { SelectProvider } from 'App/Select/SelectContext';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageHeading from 'Components/Page/PageHeading';
import { kinds } from 'Helpers/Props';
import useRootFolders, { useRootFolder } from 'RootFolder/useRootFolders';
import useSeries from 'Series/useSeries';
import { useQualityProfilesData } from 'Settings/Profiles/Quality/useQualityProfiles';
import translate from 'Utilities/String/translate';
import ImportSeriesFooter from './ImportSeriesFooter';
import ImportSeriesList from './ImportSeriesList';
import { clearImportSeries, updateImportSeriesItem } from './importSeriesStore';
import styles from './ImportSeries.module.css';

function ImportSeries() {
  const { rootFolderId: rootFolderIdString = '' } = useParams<{
    rootFolderId: string;
  }>();
  const rootFolderId = parseInt(rootFolderIdString);

  const {
    isFetching: rootFoldersFetching,
    isFetched: rootFoldersFetched,
    error: rootFoldersError,
    data: rootFolders,
  } = useRootFolders();

  useRootFolder(rootFolderId, false);

  const { path, unmappedFolders } = useMemo(() => {
    const rootFolder = rootFolders.find((r) => r.id === rootFolderId);

    return {
      path: rootFolder?.path ?? '',
      unmappedFolders:
        rootFolder?.unmappedFolders.map((unmappedFolders) => {
          return {
            ...unmappedFolders,
            id: unmappedFolders.name,
          };
        }) ?? [],
    };
  }, [rootFolders, rootFolderId]);

  const { data: librarySeries } = useSeries();

  // A folder whose name belongs to a series already in the library is matched to it, so it can be merged into it.
  useEffect(() => {
    unmappedFolders.forEach((unmappedFolder) => {
      const series = unmappedFolder.matchedSeriesId
        ? librarySeries.find((s) => s.id === unmappedFolder.matchedSeriesId)
        : undefined;

      if (series) {
        updateImportSeriesItem({
          id: unmappedFolder.id,
          selectedSeries: series,
          hasSearched: true,
        });
      }
    });
  }, [unmappedFolders, librarySeries]);

  const qualityProfiles = useQualityProfilesData();

  const defaultQualityProfileId = useAddSeriesOption('qualityProfileId');

  const scrollerRef = useRef<HTMLDivElement>(null);

  const items = useMemo(() => {
    return unmappedFolders.map((unmappedFolder) => {
      return {
        ...unmappedFolder,
        id: unmappedFolder.name,
      };
    });
  }, [unmappedFolders]);

  useEffect(() => {
    return () => {
      clearImportSeries();
    };
  }, [rootFolderId]);

  useEffect(() => {
    if (
      !defaultQualityProfileId ||
      !qualityProfiles.some((p) => p.id === defaultQualityProfileId)
    ) {
      setAddSeriesOption('qualityProfileId', qualityProfiles[0].id);
    }
  }, [defaultQualityProfileId, qualityProfiles]);

  return (
    <SelectProvider items={items}>
      <PageContent title={translate('ImportSeries')}>
        <PageContentBody ref={scrollerRef}>
          <div className={styles.content}>
            <PageHeading
              scope={translate('Media')}
              title={translate('ImportSeries')}
            />

            {rootFoldersFetching && !rootFoldersFetched ? (
              <LoadingIndicator />
            ) : null}

            {!rootFoldersFetching && !!rootFoldersError ? (
              <Alert kind={kinds.DANGER}>
                {translate('RootFoldersLoadError')}
              </Alert>
            ) : null}

            {!rootFoldersError &&
            !rootFoldersFetching &&
            rootFoldersFetched &&
            !unmappedFolders.length ? (
              <Alert kind={kinds.INFO}>
                {translate('AllSeriesInRootFolderHaveBeenImported', { path })}
              </Alert>
            ) : null}

            {!rootFoldersError &&
            rootFoldersFetched &&
            !!unmappedFolders.length &&
            scrollerRef.current ? (
              <ImportSeriesList items={items} scrollerRef={scrollerRef} />
            ) : null}
          </div>
        </PageContentBody>

        {!rootFoldersError && rootFoldersFetched && !!unmappedFolders.length ? (
          <ImportSeriesFooter />
        ) : null}
      </PageContent>
    </SelectProvider>
  );
}

export default ImportSeries;
