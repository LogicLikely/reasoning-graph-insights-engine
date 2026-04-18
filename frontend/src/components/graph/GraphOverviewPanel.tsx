import './GraphOverviewPanel.css'

interface GraphOverviewPanelProps {
  nodeCount: number
  edgeCount: number
  fixtureName: string
}

export function GraphOverviewPanel({
  nodeCount,
  edgeCount,
  fixtureName,
}: GraphOverviewPanelProps) {
  return (
    <aside className="graph-overview-panel" data-testid="graph-overview-panel">
      <span className="eyebrow">Graph Overview</span>
      <div className="graph-summary-grid">
        <div className="graph-summary-stat">
          <strong>{nodeCount}</strong>
          <span>Nodes</span>
        </div>
        <div className="graph-summary-stat">
          <strong>{edgeCount}</strong>
          <span>Edges</span>
        </div>
        <div className="graph-summary-stat">
          <strong>{fixtureName}</strong>
          <span>Fixture</span>
        </div>
      </div>
    </aside>
  )
}
