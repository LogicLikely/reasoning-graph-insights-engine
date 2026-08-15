import type { GraphAdapter } from '@logiclikely/graphmap'
import type {
  GraphFixture,
  GraphFixtureEdge,
  GraphFixtureNode,
} from '../../fixtures/sampleGraph'

function getSearchText(node: GraphFixtureNode): string {
  return [
    node.bodyText,
    node.category,
    ...(node.tags ?? []),
    node.evidence?.type,
    node.evidence?.rationale,
  ]
    .filter((value): value is string => Boolean(value))
    .join(' ')
}

/**
 * Projects the Insights API shape into GraphMap's schema-neutral topology.
 *
 * Insights stores relations child -> parent (`from` -> `to`), while GraphMap
 * compacts hierarchy parent -> child. Both meanings are kept explicitly, and
 * every canonical item retains the exact consumer-owned object in `raw`.
 */
export const insightsGraphAdapter: GraphAdapter<
  GraphFixture,
  GraphFixtureNode,
  GraphFixtureEdge
> = (graph) => ({
  rootId: graph.nodes.find((node) => node.kind === 'root')?.id,
  nodes: graph.nodes.map((node) => ({
    id: node.id,
    kind: node.kind,
    title: node.title,
    text: node.bodyText,
    search: {
      title: node.title,
      text: getSearchText(node),
    },
    raw: node,
  })),
  edges: graph.edges.map((edge) => ({
    id: edge.id,
    parentId: edge.to,
    childId: edge.from,
    sourceId: edge.from,
    targetId: edge.to,
    raw: edge,
  })),
})
