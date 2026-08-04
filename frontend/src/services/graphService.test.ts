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

  it('uses the env var to determine the default data source', async () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'true')

    const { getDefaultGraphDataSource } = await import('./graphService')

    expect(getDefaultGraphDataSource()).toBe('fixture')
  })

  it('sends the fixture graph to the counter-set API in fixture mode', async () => {
    const fixtureGraph = {
      slug: 'sample-medium',
      nodes: [
        { id: 'R1', kind: 'root', logOdds: 0 },
        { id: 'O1', kind: 'objection', logOdds: 2 },
      ],
      edges: [{ id: 'O1-R1', from: 'O1', to: 'R1', kind: 'rebut', importanceToParent: 10 }],
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

  it('posts to the reset endpoint when resetting the database', async () => {
    const postSpy = vi.fn().mockResolvedValue({})

    vi.doMock('./httpClient', () => ({
      httpClient: {
        post: postSpy,
      },
    }))

    const { resetDatabase } = await import('./graphService')

    await resetDatabase()

    expect(postSpy).toHaveBeenCalledWith('/api/graphs/reset')
  })
})
