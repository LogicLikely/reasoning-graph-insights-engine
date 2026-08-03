import type { GraphFixture, GraphFixtureNode } from '../fixtures/sampleGraph'

const COUNTER_THRESHOLD = -1

/**
 * Finds the smallest greedy set of objections that brings a target's log odds
 * below the counter threshold.  This is the fixture equivalent of the API
 * calculation and deliberately works only from the supplied graph.
 */
export function getMinimalCounterSetFromGraph(
  graph: GraphFixture,
  targetNodeId: string,
): string[] | null {
  const nodesById = new Map(graph.nodes.map((node) => [node.id, node]))
  if (!nodesById.has(targetNodeId)) {
    throw new Error(`Node '${targetNodeId}' was not found in graph '${graph.slug}'.`)
  }

  const childrenByParentId = new Map<string, GraphFixture['edges']>()
  for (const edge of graph.edges) {
    const children = childrenByParentId.get(edge.to) ?? []
    children.push(edge)
    childrenByParentId.set(edge.to, children)
  }

  const counterIds = graph.nodes
    .filter((node) => node.kind === 'objection')
    .map((node) => node.id)
  const enabledCounters = new Set<string>()
  let targetLogOdds = calculateLogOdds(targetNodeId, nodesById, childrenByParentId, enabledCounters)

  while (targetLogOdds > COUNTER_THRESHOLD) {
    const nextCounterId = counterIds
      .filter((counterId) => !enabledCounters.has(counterId))
      .map((counterId) => {
        const candidateCounters = new Set(enabledCounters).add(counterId)
        const candidateLogOdds = calculateLogOdds(
          targetNodeId,
          nodesById,
          childrenByParentId,
          candidateCounters,
        )

        return { counterId, candidateLogOdds, reduction: targetLogOdds - candidateLogOdds }
      })
      .filter((candidate) => candidate.reduction > 0)
      .sort((left, right) =>
        right.reduction - left.reduction || left.counterId.localeCompare(right.counterId),
      )[0]

    if (!nextCounterId) {
      return null
    }

    enabledCounters.add(nextCounterId.counterId)
    targetLogOdds = nextCounterId.candidateLogOdds
  }

  return [...enabledCounters]
}

function calculateLogOdds(
  nodeId: string,
  nodesById: Map<string, GraphFixtureNode>,
  childrenByParentId: Map<string, GraphFixture['edges']>,
  enabledCounters: ReadonlySet<string>,
  path = new Set<string>(),
): number {
  if (path.has(nodeId)) {
    throw new Error(`Cycle detected while calculating fixture graph likelihood at node '${nodeId}'.`)
  }

  const node = nodesById.get(nodeId)
  if (!node) {
    throw new Error(`Node '${nodeId}' is referenced by an edge but is missing from the graph.`)
  }

  const nextPath = new Set(path).add(nodeId)
  const children = childrenByParentId.get(nodeId) ?? []
  const applicableChildren = children.filter((edge) => {
    const child = nodesById.get(edge.from)
    return child?.kind !== 'objection' || enabledCounters.has(child.id)
  })

  if (applicableChildren.length === 0) {
    return node.logOdds
  }

  return applicableChildren.reduce((total, edge) => {
    const childLogOdds = calculateLogOdds(
      edge.from,
      nodesById,
      childrenByParentId,
      enabledCounters,
      nextPath,
    )
    const direction = edge.kind === 'support' ? 1 : -1
    return total + childLogOdds * direction * (edge.importanceToParent / 10)
  }, 0)
}
