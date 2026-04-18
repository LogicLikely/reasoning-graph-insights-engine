import type { Edge, Node } from '@xyflow/react'
import { MarkerType } from '@xyflow/react'
import type { GraphFixture } from '../../fixtures/sampleGraph'
import { applyDagreLayout } from './graphLayout'

export interface GraphNodeCardData extends Record<string, unknown> {
  title: string
  displayTitle: string
  kind: string
  symbol: string
  metricLabel?: string
  metricValue?: string
}

function formatKind(kind: string) {
  return kind.charAt(0).toUpperCase() + kind.slice(1)
}

function getNodeSymbol(kind: string) {
  switch (kind) {
    case 'root':
      return '🌍'
    case 'claim':
      return '🌿'
    case 'premise':
      return '🌱'
    case 'evidence':
      return '🔬'
    default:
      return '⚠️'
  }
}

function formatMetricValue(value?: number) {
  return value === undefined ? undefined : value.toFixed(2)
}

function getDisplayTitle(title: string) {
  if (title.length <= 33) {
    return title
  }

  return `${title.slice(0, 30)}...`
}

function getMetricForNode(node: GraphFixture['nodes'][number]) {
  switch (node.kind) {
    case 'root':
    case 'claim':
      return {
        metricLabel: 'Importance',
        metricValue: formatMetricValue(node.importance),
      }
    case 'premise':
    case 'counter':
      return {
        metricLabel: 'Confidence',
        metricValue: formatMetricValue(node.confidence),
      }
    case 'evidence':
      return {
        metricLabel: 'Score',
        metricValue: formatMetricValue(node.evidence?.score),
      }
    default:
      return {}
  }
}

export function mapGraphToFlow(graph: GraphFixture): {
  nodes: Node<GraphNodeCardData>[]
  edges: Edge[]
} {
  const baseNodes: Node<GraphNodeCardData>[] = graph.nodes.map((node) => ({
    id: node.id,
    type: 'default',
    data: {
      title: node.title,
      displayTitle: getDisplayTitle(node.title),
      kind: formatKind(node.kind),
      symbol: getNodeSymbol(node.kind),
      ...getMetricForNode(node),
    },
    position: { x: 0, y: 0 },
    className: `graph-node graph-node--${node.kind}`,
  }))

  const edges: Edge[] = graph.edges.map((edge) => ({
    id: edge.id,
    source: edge.from,
    target: edge.to,
    label: formatKind(edge.kind),
    markerEnd: {
      type: MarkerType.ArrowClosed,
    },
    className: `graph-edge graph-edge--${edge.kind}`,
    labelStyle: {
      fontSize: 11,
      fontWeight: 700,
    },
  }))

  return {
    nodes: applyDagreLayout(baseNodes, edges),
    edges,
  }
}
