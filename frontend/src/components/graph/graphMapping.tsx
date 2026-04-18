import type { Edge, Node } from '@xyflow/react'
import { MarkerType } from '@xyflow/react'
import type { GraphFixture } from '../../fixtures/sampleGraph'
import { applyDagreLayout } from './graphLayout'

export interface GraphNodeCardData extends Record<string, unknown> {
  id: string
  title: string
  kind: string
}

function formatKind(kind: string) {
  return kind.charAt(0).toUpperCase() + kind.slice(1)
}

export function mapGraphToFlow(graph: GraphFixture): {
  nodes: Node<GraphNodeCardData>[]
  edges: Edge[]
} {
  const baseNodes: Node<GraphNodeCardData>[] = graph.nodes.map((node) => ({
    id: node.id,
    type: 'default',
    data: {
      id: node.id,
      title: node.title,
      kind: formatKind(node.kind),
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
