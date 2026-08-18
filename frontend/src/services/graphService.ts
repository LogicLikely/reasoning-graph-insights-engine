import type { GraphFixture, GraphFixtureEdge, GraphFixtureNode } from '../fixtures/sampleGraph'
import { getGraphBySlugFromApi, getGraphCatalogFromApi } from './graphApi'
import { getGraphBySlugFromFixture } from './graphFixture'
import type { GraphSummary } from './graphTypes'
import { httpClient } from './httpClient'
import type { StressGraphId } from './stressGraphs'

export type GraphDataSource = 'fixture' | 'database'

const BENCHMARK_SET_HEADER = 'X-Insights-Benchmark-Set-Id'

function getInsightsRequestConfig(signal?: AbortSignal, benchmarkSetId?: string) {
  if (!signal && !benchmarkSetId) return undefined
  return {
    ...(signal ? { signal } : {}),
    ...(benchmarkSetId ? { headers: { [BENCHMARK_SET_HEADER]: benchmarkSetId } } : {}),
  }
}

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
  signal?: AbortSignal,
  benchmarkSetId?: string,
): Promise<string[] | null> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/nodes/${targetNodeId}/minimal-counter-set`
  const config = getInsightsRequestConfig(signal, benchmarkSetId)
  const response = config
    ? await httpClient.post<{ counterNodeIds: string[] | null }>(
      url,
      graphContext,
      config,
    )
    : await httpClient.post<{ counterNodeIds: string[] | null }>(url, graphContext)

  return response.data.counterNodeIds
}

export type BoundedNodeCounterSet = {
  counterNodeIds: string[] | null
  proofStatus: 'proven' | 'notProven'
  runNumber: number
  status: 'completed' | 'timedOut'
  stopReason: 'completed' | 'timeBudget'
  timeBudgetMilliseconds: number
}

export async function getBoundedNodeCounterSet(
  slug: string,
  targetNodeId: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
  signal?: AbortSignal,
  benchmarkSetId?: string,
): Promise<BoundedNodeCounterSet> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/nodes/${targetNodeId}/bounded-minimal-counter-set`
  const config = getInsightsRequestConfig(signal, benchmarkSetId)
  const response = config
    ? await httpClient.post<BoundedNodeCounterSet>(url, graphContext, config)
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
  benchmarkSetId?: string,
): Promise<NodeRobustness> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/least-robust-node`
  const config = getInsightsRequestConfig(signal, benchmarkSetId)
  const response = config
    ? await httpClient.post<NodeRobustness>(url, graphContext, config)
    : await httpClient.post<NodeRobustness>(url, graphContext)

  return response.data
}

export async function getNodeRobustnessRanking(
  slug: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
  signal?: AbortSignal,
  benchmarkSetId?: string,
): Promise<NodeRobustness[]> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/node-robustness-ranking`
  const config = getInsightsRequestConfig(signal, benchmarkSetId)
  const response = config
    ? await httpClient.post<NodeRobustness[]>(url, graphContext, config)
    : await httpClient.post<NodeRobustness[]>(url, graphContext)

  return response.data
}

export async function getEvidenceImpactRanking(
  slug: string,
  targetNodeId: string,
  dataSource: GraphDataSource = getDefaultGraphDataSource(),
  signal?: AbortSignal,
  benchmarkSetId?: string,
): Promise<EvidenceImpactRanking> {
  const graphContext = dataSource === 'fixture'
    ? await getGraphBySlugFromFixture(slug)
    : undefined

  const url = `/api/graphs/${slug}/nodes/${targetNodeId}/evidence-impact-ranking`
  const config = getInsightsRequestConfig(signal, benchmarkSetId)
  const response = config
    ? await httpClient.post<EvidenceImpactRanking>(url, graphContext, config)
    : await httpClient.post<EvidenceImpactRanking>(url, graphContext)

  return response.data
}

export async function updateNode(
  slug: string,
  nodeId: string,
  data: Partial<GraphFixtureNode>,
  benchmarkSetId?: string,
): Promise<void> {
  const config = getInsightsRequestConfig(undefined, benchmarkSetId)
  if (config) {
    await httpClient.patch(`/api/graphs/${slug}/nodes/${nodeId}`, data, config)
  } else {
    await httpClient.patch(`/api/graphs/${slug}/nodes/${nodeId}`, data)
  }
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
