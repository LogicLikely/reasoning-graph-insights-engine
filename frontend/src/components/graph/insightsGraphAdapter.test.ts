import { describe, expect, it } from 'vitest'
import { sampleGraph, type GraphFixture } from '../../fixtures/sampleGraph'
import { insightsGraphAdapter } from './insightsGraphAdapter'

describe('insightsGraphAdapter', () => {
  it('preserves every rich Insights object by identity and in source order', () => {
    const canonical = insightsGraphAdapter(sampleGraph)

    expect(canonical.rootId).toBe('R1')
    expect(canonical.nodes).toHaveLength(sampleGraph.nodes.length)
    expect(canonical.edges).toHaveLength(sampleGraph.edges.length)

    canonical.nodes.forEach((node, index) => {
      expect(node.raw).toBe(sampleGraph.nodes[index])
      expect(node.id).toBe(sampleGraph.nodes[index].id)
    })
    canonical.edges.forEach((edge, index) => {
      expect(edge.raw).toBe(sampleGraph.edges[index])
      expect(edge.id).toBe(sampleGraph.edges[index].id)
    })

    const evidence = canonical.nodes.find((node) => node.id === 'E1')
    expect(evidence?.raw.evidence).toBe(
      sampleGraph.nodes.find((node) => node.id === 'E1')?.evidence,
    )
  })

  it('separates GraphMap hierarchy from the native child-to-parent direction', () => {
    const canonical = insightsGraphAdapter(sampleGraph)
    const edge = canonical.edges.find((candidate) => candidate.id === 'E-R-C1')

    expect(edge).toMatchObject({
      parentId: 'R1',
      childId: 'C1',
      sourceId: 'C1',
      targetId: 'R1',
    })
    expect(edge?.raw.importanceToParent).toBe(8)
    expect(edge?.raw.kind).toBe('support')
  })

  it('indexes rich descriptive fields without treating log odds as a probability', () => {
    const canonical = insightsGraphAdapter(sampleGraph)
    const evidence = canonical.nodes.find((node) => node.id === 'E1')

    expect(evidence?.search.title).toBe('Photographs from beaches')
    expect(evidence?.search.text).toContain('Collections of beach and ocean photographs')
    expect(evidence?.search.text).toContain('observational')
    expect(evidence?.search.text).toContain('hard to interpret precisely')
    expect(evidence?.prior).toBeUndefined()
    expect(evidence?.raw.priorOdds).toBe(0.08)
    expect(evidence?.raw.posteriorOdds).toBe(0.08)
  })

  it('does not mutate frozen consumer data', () => {
    const node = Object.freeze({ ...sampleGraph.nodes[0] })
    const edge = Object.freeze({ ...sampleGraph.edges[0] })
    const graph: GraphFixture = {
      ...sampleGraph,
      nodes: [node],
      edges: [edge],
    }
    Object.freeze(graph.nodes)
    Object.freeze(graph.edges)
    Object.freeze(graph)

    expect(() => insightsGraphAdapter(graph)).not.toThrow()

    const canonical = insightsGraphAdapter(graph)
    expect(canonical.nodes[0].raw).toBe(node)
    expect(canonical.edges[0].raw).toBe(edge)
  })

  it('preserves every parent relation for a shared node in a DAG', () => {
    const sharedEvidence = sampleGraph.nodes.find((node) => node.id === 'E1')!
    const secondParentEdge = {
      id: 'E-C2-E1',
      from: sharedEvidence.id,
      to: 'C2',
      kind: 'support' as const,
      importanceToParent: 4,
    }
    const graph: GraphFixture = {
      ...sampleGraph,
      edges: [...sampleGraph.edges, secondParentEdge],
    }

    const canonical = insightsGraphAdapter(graph)
    const sharedRelations = canonical.edges.filter(
      (edge) => edge.childId === sharedEvidence.id,
    )

    expect(sharedRelations.map((edge) => edge.parentId)).toEqual(['C1', 'C2'])
    expect(sharedRelations[0].raw).toBe(sampleGraph.edges[4])
    expect(sharedRelations[1].raw).toBe(secondParentEdge)
    expect(sharedRelations[1].raw.importanceToParent).toBe(4)
  })
})
