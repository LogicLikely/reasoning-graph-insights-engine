import { useState } from 'react'
import {
  AdaptedGraphMap,
  type GraphLayoutDirection,
} from '@logiclikely/graphmap'
import { ControlButton } from '@xyflow/react'
import '@logiclikely/graphmap/style.css'
import type { GraphFixture, GraphFixtureNode } from '../../fixtures/sampleGraph'
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
  isExpanded: boolean
  onToggleExpanded: () => void
}

export function InsightsGraphCanvas({
  graph,
  selectedNodeId,
  onNodeSelect,
  isExpanded,
  onToggleExpanded,
}: InsightsGraphCanvasProps) {
  const [orientation, setOrientation] = useState<GraphLayoutDirection>('LR')

  return (
    <div className="insights-graphmap-host" data-testid="insights-graph-canvas">
      <AdaptedGraphMap
        graph={graph}
        adapter={insightsGraphAdapter}
        selectedNodeId={selectedNodeId ?? null}
        onSelect={onNodeSelect}
        orientation={orientation}
        onOrientationChange={setOrientation}
        renderNode={renderInsightsGraphNode}
        getNodePresentation={getInsightsNodePresentation}
        getEdgePresentation={getInsightsEdgePresentation}
        nodeSize={{ width: 230, height: 112 }}
        defaultNodesDraggable
        minZoom={0.35}
        maxZoom={1.5}
        additionalControls={(
          <ControlButton
            aria-label={isExpanded ? 'Restore graph size' : 'Expand graph to viewport'}
            className="insights-graphmap-expand-control"
            onClick={onToggleExpanded}
            title={isExpanded ? 'Restore graph size' : 'Expand graph to viewport'}
          >
            {isExpanded ? <RestoreSizeIcon /> : <ExpandSizeIcon />}
          </ControlButton>
        )}
        className="insights-graphmap-root"
        canvasClassName="insights-graphmap-canvas"
      />
    </div>
  )
}

function ExpandSizeIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path d="M8 3H3v5M16 3h5v5M8 21H3v-5M16 21h5v-5" />
    </svg>
  )
}

function RestoreSizeIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24">
      <path d="M8 3v5H3M16 3v5h5M8 21v-5H3M16 21v-5h5" />
    </svg>
  )
}
