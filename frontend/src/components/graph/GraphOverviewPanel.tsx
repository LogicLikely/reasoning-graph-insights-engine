import type { GraphDataSource } from '../../services/graphService'
import type { GraphSummary } from '../../services/graphTypes'
import './GraphOverviewPanel.css'

interface GraphOverviewPanelProps {
  title: string
  description: string
  nodeCount: number
  edgeCount: number
  fixtureName: string
  dataSource: GraphDataSource
  graphs?: GraphSummary[]
  selectedGraphSlug?: string
  isGraphCatalogLoading?: boolean
  isResettingDatabase?: boolean
  onDataSourceChange?: (dataSource: GraphDataSource) => void
  onGraphChange?: (slug: string) => void
  onResetDatabase?: () => void
}

export function GraphOverviewPanel({
  title,
  description,
  nodeCount,
  edgeCount,
  fixtureName,
  dataSource,
  graphs = [],
  selectedGraphSlug,
  isGraphCatalogLoading = false,
  isResettingDatabase = false,
  onDataSourceChange,
  onGraphChange,
  onResetDatabase,
}: GraphOverviewPanelProps) {
  const isUsingFixture = dataSource === 'fixture'
  const dataSourceLabel = isUsingFixture ? 'Fixture' : 'Database'

  return (
    <aside className="graph-overview-panel" data-testid="graph-overview-panel">
      <span className="eyebrow">Graph Overview</span>
      <div className="graph-overview-panel__copy">
        <h3>{title}</h3>
        <p>{description}</p>
      </div>
      <div className="graph-summary-grid">
        <div className="graph-summary-stat">
          <strong>{nodeCount}</strong>
          <span>Nodes</span>
        </div>
        <div className="graph-summary-stat">
          <strong>{edgeCount}</strong>
          <span>Edges</span>
        </div>
        <div className="graph-summary-stat graph-summary-stat--full">
          <strong>{fixtureName}</strong>
          <span>{dataSourceLabel}</span>
        </div>
      </div>
      <div className="graph-overview-panel__source-toggle" role="group" aria-label="Graph data source">
        <button
          aria-pressed={dataSource === 'fixture'}
          className="graph-overview-panel__source-option"
          onClick={() => onDataSourceChange?.('fixture')}
          type="button"
        >
          Fixture
        </button>
        <button
          aria-pressed={dataSource === 'database'}
          className="graph-overview-panel__source-option"
          onClick={() => onDataSourceChange?.('database')}
          type="button"
        >
          Database
        </button>
      </div>
      {!isUsingFixture ? (
        <label className="graph-overview-panel__graph-select">
          <span>Database graph</span>
          <select
            disabled={isGraphCatalogLoading || graphs.length === 0}
            onChange={(event) => onGraphChange?.(event.target.value)}
            value={selectedGraphSlug ?? ''}
          >
            {isGraphCatalogLoading ? (
              <option value="">Loading graphs…</option>
            ) : graphs.length === 0 ? (
              <option value="">No graphs available</option>
            ) : (
              graphs.map((graph) => (
                <option key={graph.slug} value={graph.slug}>
                  {graph.title} — {graph.slug}
                </option>
              ))
            )}
          </select>
        </label>
      ) : null}
      {!isUsingFixture && onResetDatabase ? (
        <button
          className="graph-overview-panel__reset-button"
          disabled={isResettingDatabase}
          onClick={onResetDatabase}
          type="button"
        >
          {isResettingDatabase ? 'Resetting database' : 'Reset database'}
        </button>
      ) : null}
    </aside>
  )
}
