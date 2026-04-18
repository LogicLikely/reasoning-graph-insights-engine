import {
  Background,
  Controls,
  MiniMap,
  ReactFlow,
  type Edge,
  type Node,
  type NodeMouseHandler,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import type { GraphNodeCardData } from './graphMapping'
import './GraphCanvas.css'

interface GraphCanvasProps {
  nodes: Node<GraphNodeCardData>[]
  edges: Edge[]
  selectedNodeId?: string
  onNodeSelect: (nodeId: string) => void
}

export function GraphCanvas({
  nodes,
  edges,
  selectedNodeId,
  onNodeSelect,
}: GraphCanvasProps) {
  const decoratedNodes = nodes.map((node) => ({
      ...node,
      selected: node.id === selectedNodeId,
      data: {
        ...node.data,
        label: (
          <div className="graph-node-card">
            <strong className="graph-node-card__title">
              <span className="graph-node-card__title-text">
                {node.data.symbol} {node.data.displayTitle}
              </span>
            </strong>
            {node.data.metricLabel && node.data.metricValue ? (
              <span className="graph-node-card__metric">
                {node.data.metricLabel}: {node.data.metricValue}
              </span>
            ) : null}
            <div className="graph-node-card__tooltip" role="tooltip">
              {node.data.bodyText}
            </div>
          </div>
        ),
      },
  }))

  const handleNodeClick: NodeMouseHandler<Node<GraphNodeCardData>> = (_, node) => {
    onNodeSelect(node.id)
  }

  const showMiniMap = false

  return (
    <div className="graph-canvas-shell" data-testid="graph-canvas">
      <ReactFlow
        fitView
        minZoom={0.35}
        maxZoom={1.5}
        nodes={decoratedNodes}
        edges={edges}
        onNodeClick={handleNodeClick}
        proOptions={{ hideAttribution: true }}
      >
        <Background gap={20} color="rgba(13, 93, 86, 0.08)" />
        {showMiniMap && (
          <MiniMap
            pannable
            zoomable
            nodeStrokeWidth={3}
            maskColor="rgba(247, 248, 242, 0.8)"
            nodeColor={(node) =>
              node.className?.includes('counter')
                ? '#ca5a3d'
                : node.className?.includes('evidence')
                  ? '#c2912f'
                  : '#0d5d56'
            }
          />
        )}
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  )
}
