import type { GraphFixture } from '../fixtures/sampleGraph'
import { httpClient } from './httpClient'

export async function getGraphBySlugFromApi(slug: string): Promise<GraphFixture> {
  const response = await httpClient.get<GraphFixture>(`/api/graphs/${slug}`)

  return response.data
}
