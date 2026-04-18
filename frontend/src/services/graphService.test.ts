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
})
