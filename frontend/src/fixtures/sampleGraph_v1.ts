export type GraphNodeKind = 'root' | 'claim' | 'premise' | 'counter' | 'evidence'

export interface GraphEvidenceDetails {
  type: string
  score?: number
  rationale?: string
}

export interface GraphFixtureNode {
  id: string
  kind: GraphNodeKind
  title: string
  bodyText: string
  category?: string
  tags?: string[]
  prior?: number
  confidence?: number
  weight?: number
  importance?: number
  evidence?: GraphEvidenceDetails
}

export interface GraphFixtureEdge {
  id: string
  from: string
  to: string
  kind: 'support' | 'rebut'
  weight?: number
  confidence?: number
  rationale?: string
}

export interface GraphFixture {
  slug: string
  title: string
  description: string
  nodes: GraphFixtureNode[]
  edges: GraphFixtureEdge[]
}

export const sampleGraph: GraphFixture = {
  slug: 'sample-medium',
  title: 'Sample Reasoning Graph',
  description:
    'A local fixture that mirrors the upcoming API shape and demonstrates how claims, evidence, and rebuttals relate inside the demo experience.',
  nodes: [
    {
      id: 'R1',
      kind: 'root',
      title: 'The Earth is flat',
      bodyText: 'The Earth is flat.',
      tags: ['flat-earth', 'root'],
      prior: 0.2,
      importance: 1,
    },
    {
      id: 'C1',
      kind: 'claim',
      title: 'The horizon looks flat',
      bodyText: 'The horizon appears flat to everyday observation.',
      category: 'observation',
      tags: ['visual'],
      prior: 0.55,
      importance: 0.74,
    },
    {
      id: 'C2',
      kind: 'claim',
      title: 'Water finds level',
      bodyText: 'Water seeks its level and should not conform to a sphere.',
      category: 'physical-intuition',
      prior: 0.5,
      importance: 0.71,
    },
    {
      id: 'C3',
      kind: 'claim',
      title: 'No obvious curvature from high-altitude passenger footage',
      bodyText: 'Images from balloons and planes do not show obvious curvature.',
      category: 'visual-observation',
      prior: 0.45,
      importance: 0.68,
    },
    {
      id: 'P1',
      kind: 'premise',
      title: 'Beach and ocean horizons appear straight',
      bodyText: 'At sea level, the horizon usually appears flat and level.',
      category: 'observation',
      prior: 0.68,
      confidence: 0.61,
    },
    {
      id: 'P2',
      kind: 'premise',
      title: 'Canals and lakes look level',
      bodyText: 'Large bodies of water appear level over long distances.',
      category: 'observation',
      prior: 0.64,
      confidence: 0.58,
    },
    {
      id: 'E1',
      kind: 'evidence',
      title: 'Photographs from beaches',
      bodyText: 'Collections of beach and ocean photographs are cited as visual support.',
      tags: ['observational'],
      prior: 0.52,
      evidence: {
        type: 'observational',
        score: 0.52,
        rationale:
          'Visual examples are easy to gather but hard to interpret precisely.',
      },
    },
    {
      id: 'E2',
      kind: 'evidence',
      title: 'Canal observations',
      bodyText: 'Flat-earth arguments often cite calm water surfaces and canal observations.',
      tags: ['observational'],
      prior: 0.5,
      evidence: {
        type: 'observational',
        score: 0.5,
      },
    },
    {
      id: 'O1',
      kind: 'counter',
      title: 'Human perception is a poor curvature detector',
      bodyText:
        "At normal scales, human vision is not a reliable way to detect Earth's curvature.",
      category: 'visual-limit',
      prior: 0.82,
      confidence: 0.79,
    },
    {
      id: 'O2',
      kind: 'counter',
      title: 'Atmospheric refraction affects visibility',
      bodyText:
        'Refraction can make distant objects appear higher or more visible than expected.',
      category: 'optics',
      prior: 0.84,
      confidence: 0.8,
    },
  ],
  edges: [
    {
      id: 'E-R-C1',
      from: 'C1',
      to: 'R1',
      kind: 'support',
      weight: 0.23,
    },
    {
      id: 'E-R-C2',
      from: 'C2',
      to: 'R1',
      kind: 'support',
      weight: 0.21,
    },
    {
      id: 'E-R-C3',
      from: 'C3',
      to: 'R1',
      kind: 'support',
      weight: 0.18,
    },
    {
      id: 'E-C1-P1',
      from: 'P1',
      to: 'C1',
      kind: 'support',
      weight: 0.6,
    },
    {
      id: 'E-C1-E1',
      from: 'E1',
      to: 'C1',
      kind: 'support',
      weight: 0.4,
    },
    {
      id: 'E-C2-P2',
      from: 'P2',
      to: 'C2',
      kind: 'support',
      weight: 0.55,
    },
    {
      id: 'E-C2-E2',
      from: 'E2',
      to: 'C2',
      kind: 'support',
      weight: 0.45,
    },
    {
      id: 'E-O1-P1',
      from: 'O1',
      to: 'P1',
      kind: 'rebut',
      weight: 0.75,
    },
    {
      id: 'E-O2-C3',
      from: 'O2',
      to: 'C3',
      kind: 'rebut',
      weight: 0.7,
      rationale: 'Refraction complicates long-distance visual claims.',
    },
  ],
}
