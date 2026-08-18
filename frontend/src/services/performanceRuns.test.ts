import { afterEach, describe, expect, it, vi } from 'vitest'

describe('performanceRuns', () => {
  afterEach(() => {
    vi.resetModules()
    vi.restoreAllMocks()
  })

  it('gets the complete performance report document without reshaping it', async () => {
    const report = {
      schemaVersion: 2,
      benchmarkSets: [],
      runs: [
        {
          runNumber: 17,
          startedAtUtc: '2026-08-16T18:42:31.123Z',
          algorithm: { name: 'minimal-counter-set' },
          details: { returnedNodeIds: ['O1'] },
        },
      ],
    }
    const getSpy = vi.fn().mockResolvedValue({ data: report })
    vi.doMock('./httpClient', () => ({ httpClient: { get: getSpy } }))

    const { getPerformanceRuns } = await import('./performanceRuns')

    await expect(getPerformanceRuns()).resolves.toBe(report)
    expect(getSpy).toHaveBeenCalledOnce()
    expect(getSpy).toHaveBeenCalledWith('/api/performance-runs')
  })

  it('creates a benchmark set and returns the backend-generated identity', async () => {
    const benchmarkSet = {
      id: 'set-01J8PQ0V7J9N9N4T3V6C2Q1F8A',
      name: 'LL-699 baseline',
      createdAtUtc: '2026-08-17T14:22:31.123Z',
    }
    const postSpy = vi.fn().mockResolvedValue({ data: benchmarkSet })
    vi.doMock('./httpClient', () => ({ httpClient: { post: postSpy } }))

    const { createBenchmarkSet } = await import('./performanceRuns')

    await expect(createBenchmarkSet('LL-699 baseline')).resolves.toBe(benchmarkSet)
    expect(postSpy).toHaveBeenCalledOnce()
    expect(postSpy).toHaveBeenCalledWith('/api/performance-runs/benchmark-sets', {
      name: 'LL-699 baseline',
    })
  })
})
