import type { GraphDataSource } from '../../services/graphService'
import './GraphOverviewPanel.css'

export type GraphRenderer = 'standard' | 'compact'

interface GraphOverviewPanelProps {
  title: string
  description: string
  nodeCount: number
  edgeCount: number
  fixtureName: string
  dataSource: GraphDataSource
  renderer: GraphRenderer
  isResettingDatabase?: boolean
  onDataSourceChange?: (dataSource: GraphDataSource) => void
  onRendererChange?: (renderer: GraphRenderer) => void
  onResetDatabase?: () => void
}

export function GraphOverviewPanel({
  title,
  description,
  nodeCount,
  edgeCount,
  fixtureName,
  dataSource,
  renderer,
  isResettingDatabase = false,
  onDataSourceChange,
  onRendererChange,
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
      <div className="graph-overview-panel__control">
        <span className="graph-overview-panel__control-label">Data source</span>
        <div className="graph-overview-panel__segmented-toggle" role="group" aria-label="Graph data source">
          <button
            aria-pressed={dataSource === 'fixture'}
            className="graph-overview-panel__segmented-option"
            onClick={() => onDataSourceChange?.('fixture')}
            type="button"
          >
            Fixture
          </button>
          <button
            aria-pressed={dataSource === 'database'}
            className="graph-overview-panel__segmented-option"
            onClick={() => onDataSourceChange?.('database')}
            type="button"
          >
            Database
          </button>
        </div>
      </div>
      <div className="graph-overview-panel__control">
        <span className="graph-overview-panel__control-label">Graph view</span>
        <div className="graph-overview-panel__segmented-toggle" role="group" aria-label="Graph renderer">
          <button
            aria-pressed={renderer === 'standard'}
            className="graph-overview-panel__segmented-option"
            onClick={() => onRendererChange?.('standard')}
            type="button"
          >
            Standard
          </button>
          <button
            aria-pressed={renderer === 'compact'}
            className="graph-overview-panel__segmented-option"
            onClick={() => onRendererChange?.('compact')}
            type="button"
          >
            Compact
          </button>
        </div>
      </div>
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
