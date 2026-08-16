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
        isFullscreen={false}
        onFullscreenChange={() => undefined}
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
      fullscreen: { value: false },
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
        isFullscreen={false}
        onFullscreenChange={() => undefined}
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
        isFullscreen={false}
        onFullscreenChange={() => undefined}
      />,
    )

    expect(getLatestProps().orientation).toBe('LR')
    fireEvent.click(screen.getByRole('button', { name: 'Set vertical' }))
    expect(getLatestProps().orientation).toBe('TB')
  })

  it('delegates controlled fullscreen ownership to GraphMap', () => {
    const onFullscreenChange = vi.fn()
    const { rerender } = render(
      <InsightsGraphCanvas
        graph={sampleGraph}
        onNodeSelect={() => undefined}
        isFullscreen={false}
        onFullscreenChange={onFullscreenChange}
      />,
    )

    act(() => getLatestProps().fullscreen?.onChange?.(true))
    expect(onFullscreenChange).toHaveBeenCalledWith(true)
    expect(getLatestProps().fullscreen).toMatchObject({ value: false })

    rerender(
      <InsightsGraphCanvas
        graph={sampleGraph}
        onNodeSelect={() => undefined}
        isFullscreen
        onFullscreenChange={onFullscreenChange}
      />,
    )

    expect(getLatestProps().fullscreen).toMatchObject({ value: true })
  })

  it('measures the consumer adapter without changing the GraphMap package API', () => {
    const onGraphMapAdapterMeasured = vi.fn()
    render(
      <InsightsGraphCanvas
        graph={sampleGraph}
        onNodeSelect={() => undefined}
        isFullscreen={false}
        onFullscreenChange={() => undefined}
        onGraphMapAdapterMeasured={onGraphMapAdapterMeasured}
      />,
    )

    const adapted = getLatestProps().adapter(sampleGraph)

    expect(adapted.nodes).toHaveLength(sampleGraph.nodes.length)
    expect(adapted.edges).toHaveLength(sampleGraph.edges.length)
    expect(onGraphMapAdapterMeasured).toHaveBeenCalledWith(expect.objectContaining({
      durationMilliseconds: expect.any(Number),
      startTimeMilliseconds: expect.any(Number),
      endTimeMilliseconds: expect.any(Number),
      nodeCount: sampleGraph.nodes.length,
      edgeCount: sampleGraph.edges.length,
    }))
  })
})
