export type GraphNodeKind = 'root' | 'claim' | 'evidence' | 'objection'

export type EvidenceType =
  | 'observational'
  | 'statistical'
  | 'instrumental'
  | 'documentary'
  | 'experimental'
  | 'physical'
  | 'testimony'
  | 'video'
  | 'media-analysis'
  | 'textual'

export interface GraphEvidenceDetails {
  type: EvidenceType
  score: number
  rationale?: string
}

export interface GraphFixtureNode {
  id: string
  kind: GraphNodeKind
  title: string
  bodyText: string
  category?: string
  tags?: string[]
  priorOdds: number
  posteriorOdds: number
  evidence?: GraphEvidenceDetails
}

export interface GraphFixtureEdge {
  id: string
  from: string
  to: string
  kind: 'support' | 'rebut'
  probabilityGivenParent: number
  probabilityGivenNotParent: number
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
      priorOdds: -1.15,
      posteriorOdds: -1.15,
    },
    {
      id: 'C1',
      kind: 'claim',
      title: 'The horizon looks flat',
      bodyText: 'The horizon appears flat to everyday observation.',
      category: 'observation',
      tags: ['visual'],
      priorOdds: -0.53,
      posteriorOdds: -0.53,
    },
    {
      id: 'C2',
      kind: 'claim',
      title: 'Water finds level',
      bodyText: 'Water seeks its level and should not conform to a sphere.',
      category: 'physical-intuition',
      priorOdds: -0.2,
      posteriorOdds: -0.2,
    },
    {
      id: 'C3',
      kind: 'claim',
      title: 'No obvious curvature from high-altitude passenger footage',
      bodyText: 'Images from balloons and planes do not show obvious curvature.',
      category: 'visual-observation',
      priorOdds: -0.8,
      posteriorOdds: -0.8,
    },
    {
      id: 'P1',
      kind: 'claim',
      title: 'Beach and ocean horizons appear straight',
      bodyText: 'At sea level, the horizon usually appears flat and level.',
      category: 'observation',
      priorOdds: -0.49,
      posteriorOdds: -0.49,
    },
    {
      id: 'P2',
      kind: 'claim',
      title: 'Canals and lakes look level',
      bodyText: 'Large bodies of water appear level over long distances.',
      category: 'observation',
      priorOdds: 0.2,
      posteriorOdds: 0.2,
    },
    {
      id: 'E1',
      kind: 'evidence',
      title: 'Photographs from beaches',
      bodyText: 'Collections of beach and ocean photographs are cited as visual support.',
      tags: ['observational'],
      priorOdds: 0.08,
      posteriorOdds: 0.08,
      evidence: {
        type: 'observational',
        score: 52,
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
      priorOdds: 0,
      posteriorOdds: 0,
      evidence: {
        type: 'observational',
        score: 50,
      },
    },
    {
      id: 'O1',
      kind: 'objection',
      title: 'Human perception is a poor curvature detector',
      bodyText:
        "At normal scales, human vision is not a reliable way to detect Earth's curvature.",
      category: 'visual-limit',
      priorOdds: 1.32,
      posteriorOdds: 1.32,
    },
    {
      id: 'O2',
      kind: 'objection',
      title: 'Atmospheric refraction affects visibility',
      bodyText:
        'Refraction can make distant objects appear higher or more visible than expected.',
      category: 'optics',
      priorOdds: 1.39,
      posteriorOdds: 1.39,
    },
  ],
  edges: [
    {
      id: 'E-R-C1',
      from: 'C1',
      to: 'R1',
      kind: 'support',
      probabilityGivenParent: 0.8,
      probabilityGivenNotParent: 0.1,
    },
    {
      id: 'E-R-C2',
      from: 'C2',
      to: 'R1',
      kind: 'support',
      probabilityGivenParent: 0.7,
      probabilityGivenNotParent: 0.1,
    },
    {
      id: 'E-R-C3',
      from: 'C3',
      to: 'R1',
      kind: 'support',
      probabilityGivenParent: 0.6,
      probabilityGivenNotParent: 0.1,
    },
    {
      id: 'E-C1-P1',
      from: 'P1',
      to: 'C1',
      kind: 'support',
      probabilityGivenParent: 0.7,
      probabilityGivenNotParent: 0.1,
    },
    {
      id: 'E-C1-E1',
      from: 'E1',
      to: 'C1',
      kind: 'support',
      probabilityGivenParent: 0.5,
      probabilityGivenNotParent: 0.1,
    },
    {
      id: 'E-C2-P2',
      from: 'P2',
      to: 'C2',
      kind: 'support',
      probabilityGivenParent: 0.8,
      probabilityGivenNotParent: 0.1,
    },
    {
      id: 'E-C2-E2',
      from: 'E2',
      to: 'C2',
      kind: 'support',
      probabilityGivenParent: 0.6,
      probabilityGivenNotParent: 0.1,
    },
    {
      id: 'E-O1-P1',
      from: 'O1',
      to: 'P1',
      kind: 'rebut',
      probabilityGivenParent: 0.8,
      probabilityGivenNotParent: 0.1,
    },
    {
      id: 'E-O2-C3',
      from: 'O2',
      to: 'C3',
      kind: 'rebut',
      probabilityGivenParent: 0.9,
      probabilityGivenNotParent: 0.1,
    },
  ],
}
