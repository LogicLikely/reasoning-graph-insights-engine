import type {
  GraphMapEdgePresenter,
  GraphMapNodePresenter,
  GraphMapNodeRenderer,
} from '@logiclikely/graphmap'
import { MarkerType } from '@xyflow/react'
import type { GraphFixtureEdge, GraphFixtureNode } from '../../fixtures/sampleGraph'

const nodeSymbols: Record<GraphFixtureNode['kind'], string> = {
  root: '🌍',
  claim: '🌿',
  evidence: '🔬',
  objection: '⚠️',
}

function formatLikelihood(logOdds: number): string {
  return `${(100 / (1 + Math.exp(-logOdds))).toFixed(2)}% likely`
}

export const renderInsightsGraphNode: GraphMapNodeRenderer<GraphFixtureNode> = ({
  node,
  selected,
  childCount,
}) => (
  <div
    className={[
      'insights-graphmap-card',
      childCount > 0 ? 'insights-graphmap-card--has-children' : '',
      selected ? 'insights-graphmap-card--selected' : '',
    ]
      .filter(Boolean)
      .join(' ')}
    data-insights-selected={selected ? 'true' : 'false'}
  >
    <strong className="insights-graphmap-card__title">
      <span aria-hidden="true">{nodeSymbols[node.kind]}</span>{' '}
      <span className="insights-graphmap-card__title-text">{node.title}</span>
    </strong>
    <span className="insights-graphmap-card__metric">
      {formatLikelihood(node.posteriorOdds)}
    </span>
    <div className="insights-graphmap-card__tooltip" role="tooltip">
      {node.bodyText}
    </div>
  </div>
)

export const getInsightsNodePresentation: GraphMapNodePresenter<GraphFixtureNode> = (
  node,
) => ({
  className: `insights-graphmap-node insights-graphmap-node--${node.kind}`,
})

export const getInsightsEdgePresentation: GraphMapEdgePresenter<GraphFixtureEdge> = ({
  edge,
  semanticDirectionMatchesHierarchy,
}) => ({
  className: `insights-graphmap-edge insights-graphmap-edge--${edge.kind}`,
  label: `${edge.kind === 'support' ? 'Support' : 'Rebut'} · LR ${(edge.probabilityGivenParent / edge.probabilityGivenNotParent).toFixed(3)}`,
  labelStyle: {
    fontSize: 11,
    fontWeight: 700,
  },
  ...(semanticDirectionMatchesHierarchy
    ? { markerEnd: { type: MarkerType.ArrowClosed } }
    : { markerStart: { type: MarkerType.ArrowClosed } }),
})
