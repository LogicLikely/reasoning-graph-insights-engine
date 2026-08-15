import type { GraphFixture } from '../fixtures/sampleGraph'
import { httpClient } from './httpClient'
import type { GraphSummary } from './graphTypes'

export async function getGraphCatalogFromApi(): Promise<GraphSummary[]> {
  const response = await httpClient.get<GraphSummary[]>('/api/graphs')

  return response.data
}

export async function getGraphBySlugFromApi(slug: string): Promise<GraphFixture> {
  const response = await httpClient.get<GraphFixture>(`/api/graphs/${slug}`)

  return response.data
}
