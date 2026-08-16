import { describe, expect, it } from 'vitest'
import { sampleGraph } from '../fixtures/sampleGraph'
import {
  BROWSER_JOURNEY_CONTRACT_VERSION,
  mapApiGraphToDomain,
  parseGraphMapSearchStatus,
  readBrowserJourneyConfig,
  sha256Bytes,
} from './browserJourneyContract'

describe('browserJourneyContract', () => {
  it('uses query correlation and action values ahead of preloaded defaults', () => {
    const config = readBrowserJourneyConfig(
      '?benchmarkRunId=11111111-1111-1111-1111-111111111111' +
      '&benchmarkSampleId=22222222-2222-2222-2222-222222222222' +
      '&benchmarkScenarioId=browser.graph.search.quick' +
      '&benchmarkCorrelationId=query-correlation' +
      '&benchmarkAction=search&benchmarkGraphSlug=stress-balanced-1k' +
      '&benchmarkApiBaseUrl=http%3A%2F%2F127.0.0.1%3A5010' +
      '&benchmarkSearchQuery=00001',
      {
        action: 'collapsed',
        graphSlug: 'ignored',
        correlationId: 'input-correlation',
      },
    )

    expect(config).toEqual({
      runId: '11111111-1111-1111-1111-111111111111',
      sampleId: '22222222-2222-2222-2222-222222222222',
      scenarioId: 'browser.graph.search.quick',
      correlationId: 'query-correlation',
      action: 'search',
      graphSlug: 'stress-balanced-1k',
      apiBaseUrl: 'http://127.0.0.1:5010',
      searchQuery: '00001',
    })
    expect(BROWSER_JOURNEY_CONTRACT_VERSION).toBe('phase-4-browser-v1')
  })

  it('maps the parsed API DTO into a detached consumer graph', () => {
    const mapped = mapApiGraphToDomain(sampleGraph)

    expect(mapped).toMatchObject({
      slug: sampleGraph.slug,
      title: sampleGraph.title,
      description: sampleGraph.description,
    })
    expect(mapped.nodes.map(({ id, kind, title }) => ({ id, kind, title }))).toEqual(
      sampleGraph.nodes.map(({ id, kind, title }) => ({ id, kind, title })),
    )
    expect(mapped.edges).toEqual(sampleGraph.edges)
    expect(mapped).not.toBe(sampleGraph)
    expect(mapped.nodes).not.toBe(sampleGraph.nodes)
    expect(mapped.edges).not.toBe(sampleGraph.edges)
  })

  it('rejects incomplete graph API payloads during the explicit mapping phase', () => {
    expect(() => mapApiGraphToDomain({ slug: 'broken', nodes: [], edges: [] }))
      .toThrow('graph.title must be a string')
  })

  it('labels an exact response-byte SHA without claiming canonical JSON identity', async () => {
    const bytes = new TextEncoder().encode('hello').buffer
    await expect(sha256Bytes(bytes)).resolves.toBe(
      'sha256:2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824',
    )
  })

  it('reads GraphMap search counts without recreating its matching algorithm', () => {
    expect(parseGraphMapSearchStatus('1 matching nodes · 4 total shown')).toEqual({
      statusText: '1 matching nodes · 4 total shown',
      matchCount: 1,
      requiredAncestorUnionCount: 4,
      totalResultCardinality: 1,
    })
    expect(parseGraphMapSearchStatus('No results.')).toEqual({
      statusText: 'No results.',
      matchCount: 0,
      requiredAncestorUnionCount: 0,
      totalResultCardinality: 0,
    })
    expect(parseGraphMapSearchStatus('Enter at least 3 characters to search.')).toBeNull()
  })
})
