import type { GraphFixture, GraphFixtureNode } from '../fixtures/sampleGraph'
import { getGraphBySlugFromApi } from './graphApi'
import { getGraphBySlugFromFixture } from './graphFixture'
import { httpClient } from './httpClient'

function shouldUseFixture() {
  return import.meta.env.VITE_USE_FIXTURE === 'true'
}

export async function getGraphBySlug(slug: string): Promise<GraphFixture> {
  if (shouldUseFixture()) {
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
