import dagre from 'dagre'
import { Position, type Edge, type Node } from '@xyflow/react'

const NODE_WIDTH = 230
const NODE_HEIGHT = 112

export function applyDagreLayout<
  TNodeData extends Record<string, unknown>,
  TEdgeData extends Record<string, unknown>,
>(
  nodes: Node<TNodeData>[],
  edges: Edge<TEdgeData>[],
) {
  const graph = new dagre.graphlib.Graph()

  graph.setGraph({
    rankdir: 'RL',
    nodesep: 30,
    ranksep: 100,
    marginx: 24,
    marginy: 24,
  })
  graph.setDefaultEdgeLabel(() => ({}))

  nodes.forEach((node) => {
    graph.setNode(node.id, {
      width: NODE_WIDTH,
      height: NODE_HEIGHT,
    })
  })

  edges.forEach((edge) => {
    graph.setEdge(edge.source, edge.target)
  })

  dagre.layout(graph)

  return nodes.map((node) => {
    const { x, y } = graph.node(node.id)

    return {
      ...node,
      sourcePosition: Position.Left,
      targetPosition: Position.Right,
      position: {
        x: x - NODE_WIDTH / 2,
        y: y - NODE_HEIGHT / 2,
      },
    }
  })
}
