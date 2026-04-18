import type { GraphFixture } from '../fixtures/sampleGraph'
import { getGraphBySlugFromApi } from './graphApi'
import { getGraphBySlugFromFixture } from './graphFixture'

function shouldUseFixture() {
  return import.meta.env.VITE_USE_FIXTURE === 'true'
}

export async function getGraphBySlug(slug: string): Promise<GraphFixture> {
  if (shouldUseFixture()) {
    return getGraphBySlugFromFixture(slug)
  }

  return getGraphBySlugFromApi(slug)
}
