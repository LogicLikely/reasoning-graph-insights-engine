import { describe, expect, it } from 'vitest'
import { isStressGraphId, STRESS_GRAPH_OPTIONS } from './stressGraphs'

describe('stress graph options', () => {
  it('matches the backend allowlist in canonical install order', () => {
    expect(STRESS_GRAPH_OPTIONS.map(({ id }) => id)).toEqual([
      'stress-balanced-1k',
      'stress-wide-1k',
      'stress-deep-1k',
      'stress-shared-diamond-1k',
      'stress-balanced-10k',
      'stress-wide-10k',
      'stress-deep-10k',
      'stress-shared-diamond-10k',
    ])
  })

  it('recognizes only allowlisted stress graph IDs', () => {
    STRESS_GRAPH_OPTIONS.forEach(({ id }) => {
      expect(isStressGraphId(id)).toBe(true)
    })
    expect(isStressGraphId('stress-balanced-100k')).toBe(false)
    expect(isStressGraphId('sample-medium')).toBe(false)
  })
})
