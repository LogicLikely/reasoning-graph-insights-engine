import { describe, expect, it } from 'vitest'
import { insightsNodeTitleCorpus } from './insightsNodeTitleCorpus'

const kindForIndex = (index: number) => {
  if (index === 0) return 'root'
  if (index % 5 === 0) return 'evidence'
  if (index % 10 === 2) return 'objection'
  return 'claim'
}

describe('insightsNodeTitleCorpus', () => {
  it('contains the complete ordered 180-entry review batch', () => {
    expect(insightsNodeTitleCorpus).toHaveLength(180)
    expect(new Set(insightsNodeTitleCorpus.map(({ id }) => id))).toHaveProperty(
      'size',
      180,
    )

    for (const [index, entry] of insightsNodeTitleCorpus.entries()) {
      expect(entry.id).toBe(`corpus-preview-${String(index).padStart(3, '0')}`)
      expect(entry.kind).toBe(kindForIndex(index))
      expect([...entry.title]).toHaveLength(entry.titleCharacters)
      expect(entry.title.trim().split(/\s+/).length).toBeGreaterThanOrEqual(3)
      expect(entry.title.trim().split(/\s+/).length).toBeLessThanOrEqual(6)
      expect([...entry.title].length).toBeLessThanOrEqual(35)
      expect([...entry.bodyText].length).toBeLessThanOrEqual(250)
    }
  })

  it('keeps edge cases in the final twenty positions', () => {
    expect(
      insightsNodeTitleCorpus
        .slice(0, 160)
        .every(({ sampleClass }) => sampleClass === 'representative'),
    ).toBe(true)
    expect(
      insightsNodeTitleCorpus
        .slice(160)
        .every(({ sampleClass }) => sampleClass === 'edge-case'),
    ).toBe(true)
  })
})
