import './GraphOverviewPanel.css'

interface GraphOverviewPanelProps {
  title: string
  description: string
  nodeCount: number
  edgeCount: number
  fixtureName: string
}

export function GraphOverviewPanel({
  title,
  description,
  nodeCount,
  edgeCount,
  fixtureName,
}: GraphOverviewPanelProps) {
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
          <span>Fixture</span>
        </div>
      </div>
    </aside>
  )
}
