import { useState, type CSSProperties } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent, waitFor, within } from 'storybook/test'
import {
  sampleGraph,
  type GraphFixture,
  type GraphFixtureNode,
} from '../../fixtures/sampleGraph'
import { InsightsGraphCanvas } from './InsightsGraphCanvas'

const moreNodesGraph: GraphFixture = {
  ...sampleGraph,
  slug: 'sample-compact-more',
  title: 'Sample Compact Graph with More Nodes',
  nodes: [
    ...sampleGraph.nodes,
    {
      id: 'E3',
      kind: 'evidence',
      title: 'Additional survey evidence',
      bodyText: 'A fourth direct child used to exercise compact sibling disclosure.',
      tags: ['compact-demo'],
      priorOdds: 0.1,
      posteriorOdds: 0.1,
      evidence: { type: 'statistical', score: 61 },
    },
    {
      id: 'O3',
      kind: 'objection',
      title: 'Additional root objection',
      bodyText: 'A fifth direct child used to exercise the More node.',
      category: 'compact-demo',
      priorOdds: -0.4,
      posteriorOdds: -0.4,
    },
  ],
  edges: [
    ...sampleGraph.edges,
    {
      id: 'E-R-E3',
      from: 'E3',
      to: 'R1',
      kind: 'support',
      importanceToParent: 4,
      probabilityGivenParent: 0.5,
      probabilityGivenNotParent: 0.5,
    },
    {
      id: 'E-R-O3',
      from: 'O3',
      to: 'R1',
      kind: 'rebut',
      importanceToParent: 3,
      probabilityGivenParent: 0.5,
      probabilityGivenNotParent: 0.5,
    },
  ],
}

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

function InsightsGraphCanvasHarness({ graph = sampleGraph }: { graph?: GraphFixture }) {
  const [selectedNode, setSelectedNode] = useState<GraphFixtureNode | null>(
    graph.nodes[0],
  )
  const [isFullscreen, setIsFullscreen] = useState(false)

  return (
    <div
      data-testid="insights-graph-story-stage"
      style={normalStageStyle}
    >
      <InsightsGraphCanvas
        graph={graph}
        selectedNodeId={selectedNode?.id}
        onNodeSelect={setSelectedNode}
        isFullscreen={isFullscreen}
        onFullscreenChange={setIsFullscreen}
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
          'The production Insights graph renderer backed by the vendored GraphMap package. It preserves the full Insights graph shape while progressively revealing large branches.',
      },
    },
  },
  tags: ['autodocs'],
} satisfies Meta<typeof InsightsGraphCanvasHarness>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  play: async ({ canvas, canvasElement }) => {
    await expect(canvas.getByTestId('insights-graph-canvas')).toBeVisible()

    await waitFor(() => {
      expect(canvas.getByText('The horizon looks flat')).toBeVisible()
    })

    await userEvent.click(canvas.getByText('The horizon looks flat'))
    await expect(canvas.getByTestId('insights-graph-selection')).toHaveTextContent(
      'Selected C1 · observation · posterior odds -0.53',
    )

    await userEvent.click(canvas.getByRole('button', { name: 'Expand all' }))
    await waitFor(() => {
      expect(
        canvas.getByText('Human perception is a poor curvature detector'),
      ).toBeVisible()
    })

    const claimCard = canvas.getByText('The horizon looks flat').closest('.insights-graphmap-card')
    const objectionCard = canvas
      .getByText('Human perception is a poor curvature detector')
      .closest('.insights-graphmap-card')

    expect(claimCard).not.toBeNull()
    expect(objectionCard).not.toBeNull()
    expect(getComputedStyle(objectionCard!).backgroundColor).not.toBe(
      getComputedStyle(claimCard!).backgroundColor,
    )
    expect(canvas.queryByText(/Evidence score/i)).not.toBeInTheDocument()

    await userEvent.click(
      canvas.getByRole('button', { name: 'Expand graph to viewport' }),
    )
    const body = within(canvasElement.ownerDocument.body)
    await expect(
      body.getByRole('button', { name: 'Restore graph size' }),
    ).toBeVisible()
  },
}

export const WithMoreNodes: Story = {
  args: {
    graph: moreNodesGraph,
  },
  parameters: {
    docs: {
      description: {
        story:
          'Adds five direct children to the root so the compact renderer exposes its synthetic More node without changing the production sample graph.',
      },
    },
  },
  play: async ({ canvas }) => {
    await waitFor(() => {
      expect(canvas.getByText(/More at this level \(2 hidden\)/i)).toBeVisible()
    })
  },
}
