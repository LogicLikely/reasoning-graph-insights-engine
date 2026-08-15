export const STRESS_GRAPH_OPTIONS = [
  {
    id: 'stress-balanced-1k',
    label: 'Balanced tree (1,000 nodes)',
    scale: '1K',
  },
  {
    id: 'stress-wide-1k',
    label: 'Wide star (1,000 nodes)',
    scale: '1K',
  },
  {
    id: 'stress-deep-1k',
    label: 'Deep chain (1,000 nodes)',
    scale: '1K',
  },
  {
    id: 'stress-shared-diamond-1k',
    label: 'Shared-diamond DAG (1,000 nodes)',
    scale: '1K',
  },
  {
    id: 'stress-balanced-10k',
    label: 'Balanced tree (10,000 nodes)',
    scale: '10K',
  },
  {
    id: 'stress-wide-10k',
    label: 'Wide star (10,000 nodes)',
    scale: '10K',
  },
  {
    id: 'stress-deep-10k',
    label: 'Deep chain (10,000 nodes)',
    scale: '10K',
  },
  {
    id: 'stress-shared-diamond-10k',
    label: 'Shared-diamond DAG (10,000 nodes)',
    scale: '10K',
  },
] as const

export type StressGraphId = (typeof STRESS_GRAPH_OPTIONS)[number]['id']
export type StressGraphScale = (typeof STRESS_GRAPH_OPTIONS)[number]['scale']

const stressGraphIds = new Set<string>(STRESS_GRAPH_OPTIONS.map(({ id }) => id))

export function isStressGraphId(slug: string): slug is StressGraphId {
  return stressGraphIds.has(slug)
}
