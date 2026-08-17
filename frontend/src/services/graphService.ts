import type { GraphFixture, GraphFixtureEdge, GraphFixtureNode } from '../fixtures/sampleGraph'
import { getGraphBySlugFromApi, getGraphCatalogFromApi } from './graphApi'
import { getGraphBySlugFromFixture } from './graphFixture'
import type { GraphSummary } from './graphTypes'
import { httpClient } from './httpClient'
import type { StressGraphId } from './stressGraphs'

export type GraphDataSource = 'fixture' | 'database'

export function getDefaultGraphDataSource(): GraphDataSource {
  return import.meta.env.VITE_USE_FIXTURE === 'true' ? 'fixture' : 'database'
}

export async function getGraphCatalog(): Promise<GraphSummary[]> {
  return getGraphCatalogFromApi()
}

export async function getGraphBySlug(
  slug: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
): Promise<GraphFixture> {
  if (dataSource === 'fixture') {
    return getGraphBySlugFromFixture(slug)
  }

  return getGraphBySlugFromApi(slug)
}

export async function deleteNode(slug: string, nodeId: string): Promise<void> {
  await httpClient.delete(`/api/graphs/${slug}/nodes/${nodeId}`)
}

export async function addNode(
  slug: string,
  node: GraphFixtureNode,
  parentId?: string,
  edge?: Pick<GraphFixtureEdge, 'kind' | 'importanceToParent'>,
): Promise<void> {
  await httpClient.post(`/api/graphs/${slug}/nodes`, node, {
    params: {
      parentID: parentId,
      edgeKind: edge?.kind,
      importanceToParent: edge?.importanceToParent,
    },
  })
}

export async function getNodeCounterSet(
  slug: string,
  targetNodeId: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
  signal?: AbortSignal,
): Promise<string[] | null> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/nodes/${targetNodeId}/minimal-counter-set`
  const response = signal
    ? await httpClient.post<{ counterNodeIds: string[] | null }>(
      url,
      graphContext,
      { signal },
    )
    : await httpClient.post<{ counterNodeIds: string[] | null }>(url, graphContext)

  return response.data.counterNodeIds
}

export type BoundedNodeCounterSet = {
  counterNodeIds: string[] | null
  proofStatus: 'proven' | 'notProven'
  runNumber: number
}

export async function getBoundedNodeCounterSet(
  slug: string,
  targetNodeId: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
  signal?: AbortSignal,
): Promise<BoundedNodeCounterSet> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/nodes/${targetNodeId}/bounded-minimal-counter-set`
  const response = signal
    ? await httpClient.post<BoundedNodeCounterSet>(url, graphContext, { signal })
    : await httpClient.post<BoundedNodeCounterSet>(url, graphContext)

  return response.data
}

export type EvidenceImpactRanking = {
  supportingEvidence: EvidenceImpact[]
  counterEvidence: EvidenceImpact[]
}

export type EvidenceImpact = {
  nodeId: string
  logLr: number
  probabilityDifference: number
}

export type NodeRobustness = {
  nodeId: string
  nodeTitle: string
  robustness: number
}

export async function getLeastRobustNode(
  slug: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
  signal?: AbortSignal,
): Promise<NodeRobustness> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/least-robust-node`
  const response = signal
    ? await httpClient.post<NodeRobustness>(url, graphContext, { signal })
    : await httpClient.post<NodeRobustness>(url, graphContext)

  return response.data
}

export async function getNodeRobustnessRanking(
  slug: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
  signal?: AbortSignal,
): Promise<NodeRobustness[]> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/node-robustness-ranking`
  const response = signal
    ? await httpClient.post<NodeRobustness[]>(url, graphContext, { signal })
    : await httpClient.post<NodeRobustness[]>(url, graphContext)

  return response.data
}

export async function getEvidenceImpactRanking(
  slug: string,
  targetNodeId: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
  signal?: AbortSignal,
): Promise<EvidenceImpactRanking> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/nodes/${targetNodeId}/evidence-impact-ranking`
  const response = signal
    ? await httpClient.post<EvidenceImpactRanking>(url, graphContext, { signal })
    : await httpClient.post<EvidenceImpactRanking>(url, graphContext)

  return response.data
}

export async function updateNode(
  slug: string,
  nodeId: string,
  data: Partial<GraphFixtureNode>,
): Promise<void> {
  await httpClient.patch(`/api/graphs/${slug}/nodes/${nodeId}`, data)
}

export async function addEdge(
  slug: string,
  edge: Omit<GraphFixtureEdge, 'id'> & { id?: string },
): Promise<void> {
  await httpClient.post(`/api/graphs/${slug}/edges`, edge)
}

export async function updateEdge(
  slug: string,
  edgeId: string,
  data: Partial<Pick<GraphFixtureEdge, 'importanceToParent'>>,
): Promise<void> {
  await httpClient.patch(`/api/graphs/${slug}/edges/${edgeId}`, data)
}

export async function resetDatabase(stressGraphIds: readonly StressGraphId[] = []): Promise<void> {
  await httpClient.post('/api/graphs/reset', { stressGraphIds })
}
