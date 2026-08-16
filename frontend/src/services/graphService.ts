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
  edge?: Pick<
    GraphFixtureEdge,
    | 'kind'
    | 'probabilityGivenParent'
    | 'probabilityGivenNotParent'
  >,
): Promise<void> {
  await httpClient.post(`/api/graphs/${slug}/nodes`, node, {
    params: {
      parentID: parentId,
      edgeKind: edge?.kind,
      probabilityGivenParent: edge?.probabilityGivenParent,
      probabilityGivenNotParent: edge?.probabilityGivenNotParent,
    },
  })
}

export async function getNodeCounterSet(
  slug: string,
  targetNodeId: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
): Promise<string[] | null> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const response = await httpClient.post<{
    counterNodeIds: string[] | null
  }>(`/api/graphs/${slug}/nodes/${targetNodeId}/minimal-counter-set`, graphContext)

  return response.data.counterNodeIds
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
): Promise<NodeRobustness> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const response = await httpClient.post<NodeRobustness>(
    `/api/graphs/${slug}/least-robust-node`,
    graphContext,
  )

  return response.data
}

export async function getNodeRobustnessRanking(
  slug: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
): Promise<NodeRobustness[]> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const response = await httpClient.post<NodeRobustness[]>(
    `/api/graphs/${slug}/node-robustness-ranking`,
    graphContext,
  )

  return response.data
}

export async function getEvidenceImpactRanking(
  slug: string,
  targetNodeId: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
): Promise<EvidenceImpactRanking> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const response = await httpClient.post<EvidenceImpactRanking>(
    `/api/graphs/${slug}/nodes/${targetNodeId}/evidence-impact-ranking`,
    graphContext,
  )

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
  data: Partial<Pick<
    GraphFixtureEdge,
    | 'probabilityGivenParent'
    | 'probabilityGivenNotParent'
  >>,
): Promise<void> {
  await httpClient.patch(`/api/graphs/${slug}/edges/${edgeId}`, data)
}

export async function resetDatabase(stressGraphIds: readonly StressGraphId[] = []): Promise<void> {
  await httpClient.post('/api/graphs/reset', { stressGraphIds })
}
