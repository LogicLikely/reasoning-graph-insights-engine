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

  it('sends the fixture graph to the counter-set API in fixture mode', async () => {
    const fixtureGraph = {
      slug: 'sample-medium',
      nodes: [
        { id: 'R1', kind: 'root', priorOdds: 0, posteriorOdds: 0 },
        { id: 'O1', kind: 'objection', priorOdds: 2, posteriorOdds: 2 },
      ],
      edges: [],
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

  it('sends the fixture graph to the bounded counter-set API and returns the run result', async () => {
    const fixtureGraph = {
      slug: 'sample-medium',
      nodes: [
        { id: 'R1', kind: 'root', priorOdds: 0, posteriorOdds: 0 },
        { id: 'O1', kind: 'objection', priorOdds: 2, posteriorOdds: 2 },
      ],
      edges: [],
    }
    const fixtureSpy = vi.fn().mockResolvedValue(fixtureGraph)
    const result = {
      counterNodeIds: ['O1'],
      proofStatus: 'proven',
      runNumber: 12,
      status: 'completed',
      stopReason: 'completed',
      timeBudgetMilliseconds: 120_000,
    }
    const postSpy = vi.fn().mockResolvedValue({ data: result })

    vi.doMock('./graphFixture', () => ({ getGraphBySlugFromFixture: fixtureSpy }))
    vi.doMock('./httpClient', () => ({ httpClient: { post: postSpy } }))

    const { getBoundedNodeCounterSet } = await import('./graphService')

    await expect(getBoundedNodeCounterSet('sample-medium', 'R1', 'fixture'))
      .resolves.toEqual(result)
    expect(fixtureSpy).toHaveBeenCalledWith('sample-medium')
    expect(postSpy).toHaveBeenCalledWith(
      '/api/graphs/sample-medium/nodes/R1/bounded-minimal-counter-set',
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

  it('forwards an AbortSignal to cancellable insight operations', async () => {
    const postSpy = vi.fn().mockResolvedValue({
      data: {
        counterNodeIds: ['O1'],
        proofStatus: 'proven',
        runNumber: 12,
        status: 'completed',
        stopReason: 'completed',
        timeBudgetMilliseconds: 120_000,
      },
    })
    vi.doMock('./httpClient', () => ({ httpClient: { post: postSpy } }))

    const {
      getBoundedNodeCounterSet,
      getLeastRobustNode,
      getNodeCounterSet,
      getNodeRobustnessRanking,
    } = await import('./graphService')
    const signal = new AbortController().signal

    await getNodeCounterSet('sample-medium', 'R1', 'database', signal)
    await getBoundedNodeCounterSet('sample-medium', 'R1', 'database', signal)
    await getLeastRobustNode('sample-medium', 'database', signal)
    await getNodeRobustnessRanking('sample-medium', 'database', signal)

    expect(postSpy).toHaveBeenNthCalledWith(
      1,
      '/api/graphs/sample-medium/nodes/R1/minimal-counter-set',
      undefined,
      { signal },
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      2,
      '/api/graphs/sample-medium/nodes/R1/bounded-minimal-counter-set',
      undefined,
      { signal },
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      3,
      '/api/graphs/sample-medium/least-robust-node',
      undefined,
      { signal },
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      4,
      '/api/graphs/sample-medium/node-robustness-ranking',
      undefined,
      { signal },
    )
  })

  it('keeps cancellable insight calls config-free when no signal is supplied', async () => {
    const postSpy = vi.fn().mockResolvedValue({
      data: {
        counterNodeIds: ['O1'],
        proofStatus: 'proven',
        runNumber: 12,
        status: 'completed',
        stopReason: 'completed',
        timeBudgetMilliseconds: 120_000,
      },
    })
    vi.doMock('./httpClient', () => ({ httpClient: { post: postSpy } }))

    const {
      getBoundedNodeCounterSet,
      getLeastRobustNode,
      getNodeCounterSet,
      getNodeRobustnessRanking,
    } = await import('./graphService')

    await getNodeCounterSet('sample-medium', 'R1', 'database')
    await getBoundedNodeCounterSet('sample-medium', 'R1', 'database')
    await getLeastRobustNode('sample-medium', 'database')
    await getNodeRobustnessRanking('sample-medium', 'database')

    expect(postSpy).toHaveBeenNthCalledWith(
      1,
      '/api/graphs/sample-medium/nodes/R1/minimal-counter-set',
      undefined,
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      2,
      '/api/graphs/sample-medium/nodes/R1/bounded-minimal-counter-set',
      undefined,
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      3,
      '/api/graphs/sample-medium/least-robust-node',
      undefined,
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      4,
      '/api/graphs/sample-medium/node-robustness-ranking',
      undefined,
    )
  })

  it('tags every Lab insight request with the selected benchmark set', async () => {
    const postSpy = vi.fn().mockResolvedValue({
      data: {
        counterNodeIds: [],
        proofStatus: 'proven',
        runNumber: 12,
        status: 'completed',
        stopReason: 'completed',
        timeBudgetMilliseconds: 120_000,
        supportingEvidence: [],
        counterEvidence: [],
      },
    })
    const patchSpy = vi.fn().mockResolvedValue({})
    vi.doMock('./httpClient', () => ({
      httpClient: { patch: patchSpy, post: postSpy },
    }))

    const {
      getBoundedNodeCounterSet,
      getEvidenceImpactRanking,
      getLeastRobustNode,
      getNodeCounterSet,
      getNodeRobustnessRanking,
      updateNode,
    } = await import('./graphService')
    const signal = new AbortController().signal
    const headers = { 'X-Insights-Benchmark-Set-Id': 'benchmark-01' }

    await getNodeCounterSet('sample-medium', 'R1', 'database', signal, 'benchmark-01')
    await getBoundedNodeCounterSet('sample-medium', 'R1', 'database', signal, 'benchmark-01')
    await getEvidenceImpactRanking('sample-medium', 'R1', 'database', undefined, 'benchmark-01')
    await getLeastRobustNode('sample-medium', 'database', signal, 'benchmark-01')
    await getNodeRobustnessRanking('sample-medium', 'database', signal, 'benchmark-01')
    await updateNode('sample-medium', 'E1', { priorOdds: 2 }, 'benchmark-01')

    expect(postSpy).toHaveBeenNthCalledWith(
      1,
      '/api/graphs/sample-medium/nodes/R1/minimal-counter-set',
      undefined,
      { signal, headers },
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      2,
      '/api/graphs/sample-medium/nodes/R1/bounded-minimal-counter-set',
      undefined,
      { signal, headers },
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      3,
      '/api/graphs/sample-medium/nodes/R1/evidence-impact-ranking',
      undefined,
      { headers },
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      4,
      '/api/graphs/sample-medium/least-robust-node',
      undefined,
      { signal, headers },
    )
    expect(postSpy).toHaveBeenNthCalledWith(
      5,
      '/api/graphs/sample-medium/node-robustness-ranking',
      undefined,
      { signal, headers },
    )
    expect(patchSpy).toHaveBeenCalledWith(
      '/api/graphs/sample-medium/nodes/E1',
      { priorOdds: 2 },
      { headers },
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
