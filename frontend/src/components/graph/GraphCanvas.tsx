import { useEffect, useMemo, useRef, useState } from 'react'
import {
  Background,
  ControlButton,
  Controls,
  MiniMap,
  ReactFlow,
  type Edge,
  type Node,
  type NodeMouseHandler,
  useNodesState,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import type { GraphNodeCardData } from './graphMapping'
import './GraphCanvas.css'

interface GraphCanvasProps {
  nodes: Node<GraphNodeCardData>[]
  edges: Edge[]
  selectedNodeId?: string
  onNodeSelect: (nodeId: string) => void
  isExpanded: boolean
  onToggleExpanded: () => void
}

export function GraphCanvas({
  nodes,
  edges,
  selectedNodeId,
  onNodeSelect,
  isExpanded,
  onToggleExpanded,
}: GraphCanvasProps) {
  const [nodesDraggable, setNodesDraggable] = useState(true)
  const [flowNodes, setFlowNodes, onNodesChange] = useNodesState(nodes)
  const incomingLayoutSignature = useMemo(
    () => nodes.map((node) => `${node.id}:${node.position.x}:${node.position.y}`).join('|'),
    [nodes],
  )
  const lastLayoutSignature = useRef(incomingLayoutSignature)

  useEffect(() => {
    if (lastLayoutSignature.current === incomingLayoutSignature) {
      return
    }

    lastLayoutSignature.current = incomingLayoutSignature
    setFlowNodes(nodes)
  }, [incomingLayoutSignature, nodes, setFlowNodes])

  const decoratedNodes = flowNodes.map((node) => ({
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
                {node.data.metricValue} {node.data.metricLabel}
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
        nodesDraggable={nodesDraggable}
        panOnDrag
        onNodesChange={onNodesChange}
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
              node.className?.includes('objection')
                ? '#ca5a3d'
                : node.className?.includes('evidence')
                  ? '#c2912f'
                  : '#0d5d56'
            }
          />
        )}
        <Controls showInteractive={false}>
          <ControlButton
            aria-label={nodesDraggable ? 'Lock canvas dragging' : 'Unlock canvas dragging'}
            onClick={() => setNodesDraggable((isEnabled) => !isEnabled)}
            title={nodesDraggable ? 'Lock canvas dragging' : 'Unlock canvas dragging'}
          >
            {nodesDraggable ? (
              <svg aria-hidden="true" viewBox="0 0 24 24">
                <path d="M7 11V8a5 5 0 0 1 10 0v3" />
                <path d="M6 11h12v10H6z" />
              </svg>
            ) : (
              <svg aria-hidden="true" viewBox="0 0 24 24">
                <path d="M7 11V8a5 5 0 0 1 9.2-2.75" />
                <path d="M6 11h12v10H6z" />
              </svg>
            )}
          </ControlButton>
          <ControlButton
            aria-label={isExpanded ? 'Restore graph size' : 'Expand graph to viewport'}
            onClick={onToggleExpanded}
            title={isExpanded ? 'Restore graph size' : 'Expand graph to viewport'}
          >
            {isExpanded ? (
              <svg aria-hidden="true" viewBox="0 0 24 24">
                <path d="M8 3v5H3M16 3v5h5M8 21v-5H3M16 21v-5h5" />
              </svg>
            ) : (
              <svg aria-hidden="true" viewBox="0 0 24 24">
                <path d="M8 3H3v5M16 3h5v5M8 21H3v-5M16 21h5v-5" />
              </svg>
            )}
          </ControlButton>
        </Controls>
      </ReactFlow>
    </div>
  )
}
