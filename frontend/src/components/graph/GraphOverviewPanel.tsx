import './GraphOverviewPanel.css'

interface GraphOverviewPanelProps {
  title: string
  description: string
  nodeCount: number
  edgeCount: number
  fixtureName: string
  isResettingDatabase?: boolean
  onResetDatabase?: () => void
}

export function GraphOverviewPanel({
  title,
  description,
  nodeCount,
  edgeCount,
  fixtureName,
  isResettingDatabase = false,
  onResetDatabase,
}: GraphOverviewPanelProps) {
  const isUsingFixture = import.meta.env.VITE_USE_FIXTURE === 'true'
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
