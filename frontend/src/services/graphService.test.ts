import { afterEach, describe, expect, it, vi } from 'vitest'

describe('graphService', () => {
  afterEach(() => {
    vi.resetModules()
    vi.restoreAllMocks()
    vi.unstubAllEnvs()
  })

  it('uses the fixture implementation when VITE_USE_FIXTURE is true', async () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'true')

    const fixtureSpy = vi.fn().mockResolvedValue({ slug: 'sample-medium' })
    const apiSpy = vi.fn()

    vi.doMock('./graphFixture', () => ({
      getGraphBySlugFromFixture: fixtureSpy,
    }))
    vi.doMock('./graphApi', () => ({
      getGraphBySlugFromApi: apiSpy,
    }))

    const { getGraphBySlug } = await import('./graphService')

    await expect(getGraphBySlug('sample-medium')).resolves.toEqual({
      slug: 'sample-medium',
    })
    expect(fixtureSpy).toHaveBeenCalledWith('sample-medium')
    expect(apiSpy).not.toHaveBeenCalled()
  })

  it('uses the api implementation when VITE_USE_FIXTURE is false', async () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'false')

    const fixtureSpy = vi.fn()
    const apiSpy = vi.fn().mockResolvedValue({ slug: 'sample-medium' })

    vi.doMock('./graphFixture', () => ({
      getGraphBySlugFromFixture: fixtureSpy,
    }))
    vi.doMock('./graphApi', () => ({
      getGraphBySlugFromApi: apiSpy,
    }))

    const { getGraphBySlug } = await import('./graphService')

    await expect(getGraphBySlug('sample-medium')).resolves.toEqual({
      slug: 'sample-medium',
    })
    expect(apiSpy).toHaveBeenCalledWith('sample-medium')
    expect(fixtureSpy).not.toHaveBeenCalled()
  })

  it('uses the requested data source over the env default', async () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'true')

    const fixtureSpy = vi.fn()
    const apiSpy = vi.fn().mockResolvedValue({ slug: 'sample-medium' })

    vi.doMock('./graphFixture', () => ({
      getGraphBySlugFromFixture: fixtureSpy,
    }))
    vi.doMock('./graphApi', () => ({
      getGraphBySlugFromApi: apiSpy,
    }))

    const { getGraphBySlug } = await import('./graphService')

    await expect(getGraphBySlug('sample-medium', 'database')).resolves.toEqual({
      slug: 'sample-medium',
    })
    expect(apiSpy).toHaveBeenCalledWith('sample-medium')
    expect(fixtureSpy).not.toHaveBeenCalled()
  })

  it('loads the ordered graph catalog from the api', async () => {
    const summaries = [
      {
        slug: 'sample-medium',
        title: 'Sample graph',
        description: 'First graph',
        nodeCount: 11,
        edgeCount: 10,
      },
      {
        slug: 'flat-earth-large',
        title: 'Large graph',
        description: null,
        nodeCount: 1_000,
        edgeCount: 1_248,
      },
    ]
    const catalogSpy = vi.fn().mockResolvedValue(summaries)

    vi.doMock('./graphApi', () => ({
      getGraphBySlugFromApi: vi.fn(),
      getGraphCatalogFromApi: catalogSpy,
    }))

    const { getGraphCatalog } = await import('./graphService')

    await expect(getGraphCatalog()).resolves.toEqual(summaries)
    expect(catalogSpy).toHaveBeenCalledTimes(1)
  })

  it('uses the env var to determine the default data source', async () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'true')

    const { getDefaultGraphDataSource } = await import('./graphService')

    expect(getDefaultGraphDataSource()).toBe('fixture')
  })

  it('passes all edge weights when adding a node with a parent', async () => {
    const postSpy = vi.fn().mockResolvedValue({})

    vi.doMock('./httpClient', () => ({
      httpClient: { post: postSpy },
    }))

    const { addNode } = await import('./graphService')
    const node = {
      id: 'E1',
      kind: 'evidence' as const,
      title: 'Evidence',
      bodyText: 'Evidence body',
      priorOdds: 0,
      posteriorOdds: 0,
    }
    const edge = {
      kind: 'support' as const,
      importanceToParent: 4,
      probabilityGivenParent: 0.8,
      probabilityGivenNotParent: 0.2,
    }

    await addNode('sample-medium', node, 'C1', edge)

    expect(postSpy).toHaveBeenCalledWith('/api/graphs/sample-medium/nodes', node, {
      params: {
        parentID: 'C1',
        edgeKind: 'support',
        importanceToParent: 4,
        probabilityGivenParent: 0.8,
        probabilityGivenNotParent: 0.2,
      },
    })
  })

  it('posts both conditional probabilities when adding an edge', async () => {
    const postSpy = vi.fn().mockResolvedValue({})

    vi.doMock('./httpClient', () => ({
      httpClient: { post: postSpy },
    }))

    const { addEdge } = await import('./graphService')
    const edge = {
      from: 'E1',
      to: 'C1',
      kind: 'support' as const,
      importanceToParent: 4,
      probabilityGivenParent: 0.8,
      probabilityGivenNotParent: 0.2,
    }

    await addEdge('sample-medium', edge)

    expect(postSpy).toHaveBeenCalledWith('/api/graphs/sample-medium/edges', edge)
  })

  it('patches importance and both conditional probabilities together', async () => {
    const patchSpy = vi.fn().mockResolvedValue({})

    vi.doMock('./httpClient', () => ({
      httpClient: { patch: patchSpy },
    }))

    const { updateEdge } = await import('./graphService')
    const update = {
      importanceToParent: 6,
      probabilityGivenParent: 0.7,
      probabilityGivenNotParent: 0.3,
    }

    await updateEdge('sample-medium', 'E-C1-E1', update)

    expect(patchSpy).toHaveBeenCalledWith(
      '/api/graphs/sample-medium/edges/E-C1-E1',
      update,
    )
  })

  it('sends the fixture graph to the counter-set API in fixture mode', async () => {
    const fixtureGraph = {
      slug: 'sample-medium',
      nodes: [
        { id: 'R1', kind: 'root', priorOdds: 0, posteriorOdds: 0 },
        { id: 'O1', kind: 'objection', priorOdds: 2, posteriorOdds: 2 },
      ],
      edges: [{
        id: 'O1-R1',
        from: 'O1',
        to: 'R1',
        kind: 'rebut',
        importanceToParent: 10,
        probabilityGivenParent: 0.5,
        probabilityGivenNotParent: 0.5,
      }],
    }
    const fixtureSpy = vi.fn().mockResolvedValue(fixtureGraph)
    const postSpy = vi.fn().mockResolvedValue({ data: { counterNodeIds: ['O1'] } })

    vi.doMock('./graphFixture', () => ({ getGraphBySlugFromFixture: fixtureSpy }))
    vi.doMock('./httpClient', () => ({ httpClient: { post: postSpy } }))

    const { getNodeCounterSet } = await import('./graphService')

    await expect(getNodeCounterSet('sample-medium', 'R1', 'fixture')).resolves.toEqual(['O1'])
    expect(fixtureSpy).toHaveBeenCalledWith('sample-medium')
    expect(postSpy).toHaveBeenCalledWith(
      '/api/graphs/sample-medium/nodes/R1/minimal-counter-set',
      fixtureGraph,
    )
  })

  it('sends the fixture graph to the evidence-impact API and returns its sorted IDs', async () => {
    const fixtureGraph = {
      slug: 'sample-medium',
      nodes: [{ id: 'R1', kind: 'root', priorOdds: 0, posteriorOdds: 0 }],
      edges: [],
    }
    const fixtureSpy = vi.fn().mockResolvedValue(fixtureGraph)
    const ranking = {
      supportingEvidence: [
        { nodeId: 'E2', logLr: 0.52, probabilityDifference: 0.1 },
        { nodeId: 'E1', logLr: 0.47, probabilityDifference: 0.09 },
      ],
      counterEvidence: [
        { nodeId: 'O3', logLr: -1.87, probabilityDifference: -0.3 },
        { nodeId: 'O2', logLr: -1.53, probabilityDifference: -0.25 },
        { nodeId: 'O1', logLr: -1.49, probabilityDifference: -0.2 },
      ],
    }
    const postSpy = vi.fn().mockResolvedValue({ data: ranking })

    vi.doMock('./graphFixture', () => ({ getGraphBySlugFromFixture: fixtureSpy }))
    vi.doMock('./httpClient', () => ({ httpClient: { post: postSpy } }))

    const { getEvidenceImpactRanking } = await import('./graphService')

    await expect(getEvidenceImpactRanking('sample-medium', 'R1', 'fixture'))
      .resolves.toEqual(ranking)
    expect(postSpy).toHaveBeenCalledWith(
      '/api/graphs/sample-medium/nodes/R1/evidence-impact-ranking',
      fixtureGraph,
    )
  })

  it('posts selected stress graph IDs to the reset endpoint', async () => {
    const postSpy = vi.fn().mockResolvedValue({})

    vi.doMock('./httpClient', () => ({
      httpClient: {
        post: postSpy,
      },
    }))

    const { resetDatabase } = await import('./graphService')

    await resetDatabase(['stress-balanced-1k', 'stress-deep-10k'])

    expect(postSpy).toHaveBeenCalledWith('/api/graphs/reset', {
      stressGraphIds: ['stress-balanced-1k', 'stress-deep-10k'],
    })
  })

  it('requests only standard graphs when reset options are omitted', async () => {
    const postSpy = vi.fn().mockResolvedValue({})

    vi.doMock('./httpClient', () => ({
      httpClient: {
        post: postSpy,
      },
    }))

    const { resetDatabase } = await import('./graphService')

    await resetDatabase()

    expect(postSpy).toHaveBeenCalledWith('/api/graphs/reset', { stressGraphIds: [] })
  })
})
