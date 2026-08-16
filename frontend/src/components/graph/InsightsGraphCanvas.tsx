import { useCallback, useState } from 'react'
import {
  AdaptedGraphMap,
  type GraphAdapter,
  type GraphLayoutDirection,
} from '@logiclikely/graphmap'
import '@logiclikely/graphmap/style.css'
import type {
  GraphFixture,
  GraphFixtureEdge,
  GraphFixtureNode,
} from '../../fixtures/sampleGraph'
import { insightsGraphAdapter } from './insightsGraphAdapter'
import {
  getInsightsEdgePresentation,
  getInsightsNodePresentation,
  renderInsightsGraphNode,
} from './insightsGraphPresentation'
import './InsightsGraphCanvas.css'

export interface InsightsGraphCanvasProps {
  graph: GraphFixture
  selectedNodeId?: string | null
  onNodeSelect: (node: GraphFixtureNode | null) => void
  isFullscreen: boolean
  onFullscreenChange: (isFullscreen: boolean) => void
  onGraphMapAdapterMeasured?: (measurement: InsightsGraphAdapterMeasurement) => void
}

export interface InsightsGraphAdapterMeasurement {
  durationMilliseconds: number
  startTimeMilliseconds: number
  endTimeMilliseconds: number
  nodeCount: number
  edgeCount: number
}

export function InsightsGraphCanvas({
  graph,
  selectedNodeId,
  onNodeSelect,
  isFullscreen,
  onFullscreenChange,
  onGraphMapAdapterMeasured,
}: InsightsGraphCanvasProps) {
  const [orientation, setOrientation] = useState<GraphLayoutDirection>('LR')
  const measuredAdapter = useCallback<GraphAdapter<GraphFixture, GraphFixtureNode, GraphFixtureEdge>>((currentGraph) => {
    const startTimeMilliseconds = performance.now()
    const adaptedGraph = insightsGraphAdapter(currentGraph)
    const endTimeMilliseconds = performance.now()

    onGraphMapAdapterMeasured?.({
      durationMilliseconds: endTimeMilliseconds - startTimeMilliseconds,
      startTimeMilliseconds,
      endTimeMilliseconds,
      nodeCount: adaptedGraph.nodes.length,
      edgeCount: adaptedGraph.edges.length,
    })

    return adaptedGraph
  }, [onGraphMapAdapterMeasured])

  return (
    <div className="insights-graphmap-host" data-testid="insights-graph-canvas">
      <AdaptedGraphMap
        graph={graph}
        adapter={onGraphMapAdapterMeasured ? measuredAdapter : insightsGraphAdapter}
        selectedNodeId={selectedNodeId ?? null}
        onSelect={onNodeSelect}
        defaultTheme="insights"
        orientation={orientation}
        onOrientationChange={setOrientation}
        renderNode={renderInsightsGraphNode}
        getNodePresentation={getInsightsNodePresentation}
        getEdgePresentation={getInsightsEdgePresentation}
        nodeSize={{ width: 230, height: 112 }}
        defaultNodesDraggable
        minZoom={0.35}
        maxZoom={1.5}
        fullscreen={{ value: isFullscreen, onChange: onFullscreenChange }}
        className="insights-graphmap-root"
        canvasClassName="insights-graphmap-canvas"
      />
    </div>
  )
}
