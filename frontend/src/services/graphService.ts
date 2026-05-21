import type { GraphFixture } from '../fixtures/sampleGraph'
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
