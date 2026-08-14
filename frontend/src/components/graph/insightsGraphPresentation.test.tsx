import { render, screen } from '@testing-library/react'
import type {
  GraphMapEdgeRenderContext,
  GraphMapNodeRenderContext,
} from '@logiclikely/graphmap'
import { MarkerType } from '@xyflow/react'
import { describe, expect, it } from 'vitest'
import { sampleGraph } from '../../fixtures/sampleGraph'
import { insightsGraphAdapter } from './insightsGraphAdapter'
import {
  getInsightsEdgePresentation,
  getInsightsNodePresentation,
  renderInsightsGraphNode,
} from './insightsGraphPresentation'

describe('Insights GraphMap presentation', () => {
  it('renders likelihood, selection, and the full body tooltip without duplicating evidence details', () => {
    const canonical = insightsGraphAdapter(sampleGraph)
    const graphNode = canonical.nodes.find((node) => node.id === 'E1')!
    const context: GraphMapNodeRenderContext<(typeof graphNode)['raw']> = {
      node: graphNode.raw,
      graphNode,
      id: graphNode.id,
      kind: graphNode.kind,
      title: graphNode.title,
      text: graphNode.text,
      selected: true,
      childCount: 0,
      hiddenCount: 0,
      expanded: false,
      onToggle: () => undefined,
      orientation: 'LR',
      width: 230,
      height: 112,
    }

    render(renderInsightsGraphNode(context))

    expect(screen.getByText('Photographs from beaches')).toBeInTheDocument()
    expect(screen.getByText('52.00% likely')).toBeInTheDocument()
    expect(screen.queryByText(/Evidence score/i)).not.toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent(
      'Collections of beach and ocean photographs are cited as visual support.',
    )
    expect(screen.getByText('52.00% likely').parentElement).toHaveAttribute(
      'data-insights-selected',
      'true',
    )
  })

  it('assigns stable node-kind classes without changing the raw node', () => {
    const node = sampleGraph.nodes.find((candidate) => candidate.id === 'O1')!
    const graphNode = insightsGraphAdapter(sampleGraph).nodes.find(
      (candidate) => candidate.id === node.id,
    )!

    expect(getInsightsNodePresentation(node, graphNode)).toEqual({
      className: 'insights-graphmap-node insights-graphmap-node--objection',
    })
  })

  it('styles and labels importance edges while preserving their semantic arrow direction', () => {
    const graphEdge = insightsGraphAdapter(sampleGraph).edges[0]
    const context: GraphMapEdgeRenderContext<(typeof graphEdge)['raw']> = {
      edge: graphEdge.raw,
      graphEdge,
      semanticDirectionMatchesHierarchy: false,
    }

    const presentation = getInsightsEdgePresentation(context)

    expect(presentation).toMatchObject({
      className: 'insights-graphmap-edge insights-graphmap-edge--support',
      label: 'Support · 8',
      markerStart: { type: MarkerType.ArrowClosed },
    })
    expect(presentation).not.toHaveProperty('markerEnd')
    expect(graphEdge.raw).toBe(sampleGraph.edges[0])
  })

  it('moves the marker to the hierarchy target when semantic direction already matches', () => {
    const graphEdge = insightsGraphAdapter(sampleGraph).edges[0]
    const presentation = getInsightsEdgePresentation({
      edge: graphEdge.raw,
      graphEdge,
      semanticDirectionMatchesHierarchy: true,
    })

    expect(presentation).toMatchObject({
      markerEnd: { type: MarkerType.ArrowClosed },
    })
    expect(presentation).not.toHaveProperty('markerStart')
  })
})
