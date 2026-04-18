import type { GraphFixtureNode } from '../../fixtures/sampleGraph'

interface GraphDetailsPanelProps {
  node?: GraphFixtureNode
}

function formatMetric(value?: number) {
  return value === undefined ? null : value.toFixed(2)
}

export function GraphDetailsPanel({ node }: GraphDetailsPanelProps) {
  if (!node) {
    return (
      <aside className="graph-details-panel" data-testid="graph-details-panel">
        <span className="eyebrow">Node Details</span>
        <div className="graph-details-panel__empty">
          <h3>Select a node to view details</h3>
          <p>
            Click any node in the graph to inspect its role, supporting text, and
            any attached metadata in this panel.
          </p>
        </div>
      </aside>
    )
  }

  return (
    <aside className="graph-details-panel" data-testid="graph-details-panel">
      <span className="eyebrow">Node Details</span>

      <div className="graph-details-panel__header">
        <h3>{node.title}</h3>
        <div className="graph-details-panel__meta">
          <span>{node.kind}</span>
          <span>{node.id}</span>
        </div>
      </div>

      <p className="graph-details-panel__body">{node.bodyText}</p>

      <dl className="graph-details-list">
        {node.category ? (
          <>
            <dt>Category</dt>
            <dd>{node.category}</dd>
          </>
        ) : null}
        {node.tags?.length ? (
          <>
            <dt>Tags</dt>
            <dd>
              <div className="graph-tag-list">
                {node.tags.map((tag) => (
                  <span key={tag} className="graph-tag">
                    {tag}
                  </span>
                ))}
              </div>
            </dd>
          </>
        ) : null}
        {formatMetric(node.prior) ? (
          <>
            <dt>Prior</dt>
            <dd>{formatMetric(node.prior)}</dd>
          </>
        ) : null}
        {formatMetric(node.confidence) ? (
          <>
            <dt>Confidence</dt>
            <dd>{formatMetric(node.confidence)}</dd>
          </>
        ) : null}
        {formatMetric(node.weight) ? (
          <>
            <dt>Weight</dt>
            <dd>{formatMetric(node.weight)}</dd>
          </>
        ) : null}
        {formatMetric(node.importance) ? (
          <>
            <dt>Importance</dt>
            <dd>{formatMetric(node.importance)}</dd>
          </>
        ) : null}
        {node.evidence ? (
          <>
            <dt>Evidence</dt>
            <dd className="graph-evidence-block">
              <strong>{node.evidence.type}</strong>
              {node.evidence.score !== undefined ? (
                <span>Score: {node.evidence.score.toFixed(2)}</span>
              ) : null}
              {node.evidence.rationale ? <p>{node.evidence.rationale}</p> : null}
            </dd>
          </>
        ) : null}
      </dl>
    </aside>
  )
}
