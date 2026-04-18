import { sampleGraph, type GraphFixture } from '../fixtures/sampleGraph'

export async function getGraphBySlugFromFixture(
  slug: string,
): Promise<GraphFixture> {
  if (slug !== sampleGraph.slug) {
    throw new Error(`Fixture graph '${slug}' was not found.`)
  }

  return sampleGraph
}
