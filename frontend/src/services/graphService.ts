import type { GraphFixture, GraphFixtureNode } from '../fixtures/sampleGraph'
import { getGraphBySlugFromApi } from './graphApi'
import { getGraphBySlugFromFixture } from './graphFixture'
import { httpClient } from './httpClient'

export type GraphDataSource = 'fixture' | 'database'

export function getDefaultGraphDataSource(): GraphDataSource {
  return import.meta.env.VITE_USE_FIXTURE === 'true' ? 'fixture' : 'database'
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
): Promise<void> {
  await httpClient.post(`/api/graphs/${slug}/nodes`, node, {
    params: { parentID: parentId },
  })
}

export async function updateNode(
  slug: string,
  nodeId: string,
  data: Partial<GraphFixtureNode>,
): Promise<void> {
  await httpClient.patch(`/api/graphs/${slug}/nodes/${nodeId}`, data)
}

export async function resetDatabase(): Promise<void> {
  await httpClient.post('/api/graphs/reset')
}
