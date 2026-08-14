import type { ReactNode } from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import type { AdaptedGraphMapProps } from '@logiclikely/graphmap'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type {
  GraphFixture,
  GraphFixtureEdge,
  GraphFixtureNode,
} from '../../fixtures/sampleGraph'
import { sampleGraph } from '../../fixtures/sampleGraph'
import { InsightsGraphCanvas } from './InsightsGraphCanvas'
import { insightsGraphAdapter } from './insightsGraphAdapter'
import {
  getInsightsEdgePresentation,
  getInsightsNodePresentation,
  renderInsightsGraphNode,
} from './insightsGraphPresentation'

const adaptedGraphMapMock = vi.hoisted(() => vi.fn())

type CapturedProps = AdaptedGraphMapProps<
  GraphFixture,
  GraphFixtureNode,
  GraphFixtureEdge
>

vi.mock('@logiclikely/graphmap', () => ({
  AdaptedGraphMap: (props: CapturedProps) => {
    adaptedGraphMapMock(props)

    return (
      <div data-testid="adapted-graphmap-mock">
        {props.additionalControls as ReactNode}
        <button type="button" onClick={() => props.onOrientationChange?.('TB')}>
          Set vertical
        </button>
      </div>
    )
  },
}))

function getLatestProps(): CapturedProps {
  return adaptedGraphMapMock.mock.calls.at(-1)?.[0] as CapturedProps
}

describe('InsightsGraphCanvas', () => {
  beforeEach(() => {
    adaptedGraphMapMock.mockClear()
  })

  it('wires the rich adapter and presentation into the generic GraphMap package', () => {
    render(
      <InsightsGraphCanvas
        graph={sampleGraph}
        selectedNodeId="E1"
        onNodeSelect={() => undefined}
        isExpanded={false}
        onToggleExpanded={() => undefined}
      />,
    )

    expect(screen.getByTestId('insights-graph-canvas')).toBeInTheDocument()
    expect(getLatestProps()).toMatchObject({
      graph: sampleGraph,
      adapter: insightsGraphAdapter,
      selectedNodeId: 'E1',
      defaultTheme: 'insights',
      orientation: 'LR',
      renderNode: renderInsightsGraphNode,
      getNodePresentation: getInsightsNodePresentation,
      getEdgePresentation: getInsightsEdgePresentation,
      nodeSize: { width: 230, height: 112 },
      defaultNodesDraggable: true,
      minZoom: 0.35,
      maxZoom: 1.5,
      className: 'insights-graphmap-root',
      canvasClassName: 'insights-graphmap-canvas',
    })
  })

  it('passes the exact raw node and null selection back to Insights', () => {
    const onNodeSelect = vi.fn()
    render(
      <InsightsGraphCanvas
        graph={sampleGraph}
        onNodeSelect={onNodeSelect}
        isExpanded={false}
        onToggleExpanded={() => undefined}
      />,
    )

    const props = getLatestProps()
    const evidence = sampleGraph.nodes.find((node) => node.id === 'E1')!

    act(() => props.onSelect?.(evidence))
    act(() => props.onSelect?.(null))

    expect(onNodeSelect.mock.calls[0][0]).toBe(evidence)
    expect(onNodeSelect).toHaveBeenLastCalledWith(null)
    expect(props.selectedNodeId).toBeNull()
  })

  it('keeps the orientation control fully controlled by the wrapper', () => {
    render(
      <InsightsGraphCanvas
        graph={sampleGraph}
        onNodeSelect={() => undefined}
        isExpanded={false}
        onToggleExpanded={() => undefined}
      />,
    )

    expect(getLatestProps().orientation).toBe('LR')
    fireEvent.click(screen.getByRole('button', { name: 'Set vertical' }))
    expect(getLatestProps().orientation).toBe('TB')
  })

  it('delegates fullscreen ownership and updates the host control label', () => {
    const onToggleExpanded = vi.fn()
    const { rerender } = render(
      <InsightsGraphCanvas
        graph={sampleGraph}
        onNodeSelect={() => undefined}
        isExpanded={false}
        onToggleExpanded={onToggleExpanded}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Expand graph to viewport' }))
    expect(onToggleExpanded).toHaveBeenCalledOnce()

    rerender(
      <InsightsGraphCanvas
        graph={sampleGraph}
        onNodeSelect={() => undefined}
        isExpanded
        onToggleExpanded={onToggleExpanded}
      />,
    )

    expect(screen.getByRole('button', { name: 'Restore graph size' })).toBeInTheDocument()
  })
})
