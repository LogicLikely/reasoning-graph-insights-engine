import { useState, type CSSProperties } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent, waitFor } from 'storybook/test'
import { sampleGraph, type GraphFixtureNode } from '../../fixtures/sampleGraph'
import { InsightsGraphCanvas } from './InsightsGraphCanvas'

const normalStageStyle: CSSProperties = {
  display: 'grid',
  gridTemplateRows: 'minmax(34rem, 1fr) auto',
  gap: '0.75rem',
  width: '100%',
  minHeight: '38rem',
  boxSizing: 'border-box',
  padding: '1rem',
  background: '#eef2e6',
}

const expandedStageStyle: CSSProperties = {
  ...normalStageStyle,
  position: 'fixed',
  inset: 0,
  zIndex: 1000,
  height: '100dvh',
}

function InsightsGraphCanvasHarness() {
  const [selectedNode, setSelectedNode] = useState<GraphFixtureNode | null>(
    sampleGraph.nodes[0],
  )
  const [isExpanded, setIsExpanded] = useState(false)

  return (
    <div
      data-testid="insights-graph-story-stage"
      style={isExpanded ? expandedStageStyle : normalStageStyle}
    >
      <InsightsGraphCanvas
        graph={sampleGraph}
        selectedNodeId={selectedNode?.id}
        onNodeSelect={setSelectedNode}
        isExpanded={isExpanded}
        onToggleExpanded={() => setIsExpanded((current) => !current)}
      />
      <output
        data-testid="insights-graph-selection"
        style={{ color: '#16302c', font: '600 0.9rem/1.4 system-ui, sans-serif' }}
      >
        {selectedNode
          ? `Selected ${selectedNode.id} · ${selectedNode.category ?? selectedNode.kind} · posterior odds ${selectedNode.posteriorOdds}`
          : 'No node selected'}
      </output>
    </div>
  )
}

const meta = {
  title: 'Graph/InsightsGraphCanvas',
  component: InsightsGraphCanvasHarness,
  parameters: {
    layout: 'fullscreen',
    docs: {
      description: {
        component:
          'An isolated integration harness for the vendored GraphMap package. It adapts the full Insights graph shape without replacing the production DemoPage renderer yet.',
      },
    },
  },
  tags: ['autodocs'],
} satisfies Meta<typeof InsightsGraphCanvasHarness>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  play: async ({ canvas }) => {
    await expect(canvas.getByTestId('insights-graph-canvas')).toBeVisible()

    await waitFor(() => {
      expect(canvas.getByText('The horizon looks flat')).toBeVisible()
    })

    await userEvent.click(canvas.getByText('The horizon looks flat'))
    await expect(canvas.getByTestId('insights-graph-selection')).toHaveTextContent(
      'Selected C1 · observation · posterior odds -0.53',
    )

    await userEvent.click(
      canvas.getByRole('button', { name: 'Expand graph to viewport' }),
    )
    await expect(
      canvas.getByRole('button', { name: 'Restore graph size' }),
    ).toBeVisible()
  },
}
