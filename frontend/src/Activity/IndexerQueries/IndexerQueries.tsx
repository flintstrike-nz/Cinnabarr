import React from 'react';
import FieldSet from 'Components/FieldSet';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import { useIndexerQueries, useIndexerQueryStats } from './useIndexerQueries';

const statsColumns: Column[] = [
  { name: 'indexer', label: () => translate('Indexer'), isVisible: true },
  { name: 'queries', label: () => translate('Queries'), isVisible: true },
  { name: 'failures', label: () => translate('Failures'), isVisible: true },
  {
    name: 'averageElapsed',
    label: () => translate('AverageResponseTime'),
    isVisible: true,
  },
  { name: 'lastQuery', label: () => translate('LastQuery'), isVisible: true },
];

const queryColumns: Column[] = [
  { name: 'date', label: () => translate('Date'), isVisible: true },
  { name: 'indexer', label: () => translate('Indexer'), isVisible: true },
  { name: 'query', label: () => translate('Query'), isVisible: true },
  { name: 'results', label: () => translate('Results'), isVisible: true },
  { name: 'elapsed', label: () => translate('ResponseTime'), isVisible: true },
  { name: 'status', label: () => translate('Status'), isVisible: true },
];

function IndexerQueries() {
  const queries = useIndexerQueries();
  const stats = useIndexerQueryStats();

  return (
    <PageContent title={translate('IndexerQueries')}>
      <PageContentBody>
        {queries.isFetching && !queries.data.length ? (
          <LoadingIndicator />
        ) : null}

        <FieldSet legend={translate('IndexerQueriesLastSevenDays')}>
          {stats.data.length ? (
            <Table columns={statsColumns}>
              <TableBody>
                {stats.data.map((item) => (
                  <TableRow key={item.indexerId}>
                    <TableRowCell>{item.indexerName}</TableRowCell>
                    <TableRowCell>{item.queries}</TableRowCell>
                    <TableRowCell>{item.failures}</TableRowCell>
                    <TableRowCell>{item.averageElapsedMs} ms</TableRowCell>
                    <RelativeDateCell date={item.lastQuery} />
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            translate('IndexerQueriesNone')
          )}
        </FieldSet>

        <FieldSet legend={translate('IndexerQueriesRecent')}>
          {queries.data.length ? (
            <Table columns={queryColumns}>
              <TableBody>
                {queries.data.map((item) => (
                  <TableRow key={item.id}>
                    <RelativeDateCell date={item.date} />
                    <TableRowCell>{item.indexerName}</TableRowCell>
                    <TableRowCell>{item.query}</TableRowCell>
                    <TableRowCell>{item.resultCount}</TableRowCell>
                    <TableRowCell>{item.elapsedMs} ms</TableRowCell>
                    <TableRowCell title={item.error}>
                      {item.successful
                        ? translate('Successful')
                        : translate('Failed')}
                    </TableRowCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : null}
        </FieldSet>
      </PageContentBody>
    </PageContent>
  );
}

export default IndexerQueries;
