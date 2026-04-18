import { describe, expect, it } from 'vitest'
import { sampleGraph } from '../../fixtures/sampleGraph'
import { mapGraphToFlow } from './graphMapping'

describe('mapGraphToFlow', () => {
  it('maps fixture nodes and edges into React Flow shapes', () => {
    const flow = mapGraphToFlow(sampleGraph)

    expect(flow.nodes).toHaveLength(sampleGraph.nodes.length)
    expect(flow.edges).toHaveLength(sampleGraph.edges.length)
    expect(flow.nodes[0]).toMatchObject({
      id: sampleGraph.nodes[0].id,
      type: 'default',
    })
    expect(flow.edges[0]).toMatchObject({
      id: sampleGraph.edges[0].id,
      source: sampleGraph.edges[0].from,
      target: sampleGraph.edges[0].to,
    })
  })

  it('assigns laid out positions instead of leaving every node at the origin', () => {
    const flow = mapGraphToFlow(sampleGraph)

    const positionedNodes = flow.nodes.filter(
      (node) => node.position.x !== 0 || node.position.y !== 0,
    )

    expect(positionedNodes.length).toBeGreaterThan(0)
  })
})
