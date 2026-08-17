import { afterEach, describe, expect, it, vi } from 'vitest'

describe('performanceRuns', () => {
  afterEach(() => {
    vi.resetModules()
    vi.restoreAllMocks()
  })

  it('gets the complete performance report document without reshaping it', async () => {
    const report = {
      schemaVersion: 1,
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
})
