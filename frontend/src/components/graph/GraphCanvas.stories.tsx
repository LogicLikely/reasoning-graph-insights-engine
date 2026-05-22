import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { GraphCanvas } from './GraphCanvas'
import { sampleGraph } from '../../fixtures/sampleGraph'
import { mapGraphToFlow } from './graphMapping'

const flowGraph = mapGraphToFlow(sampleGraph)

const meta = {
  component: GraphCanvas,
  decorators: [
    (Story) => (
      <div style={{ height: '34rem', width: '100%' }}>
        <Story />
      </div>
    ),
  ],
  parameters: {
    layout: 'fullscreen',
  },
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof GraphCanvas>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  args: {
    nodes: flowGraph.nodes,
    edges: flowGraph.edges,
    selectedNodeId: 'R1',
    onNodeSelect: () => undefined,
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByTestId('graph-canvas')).toBeVisible()
    // await expect(canvas.getByText(/The Earth is flat/i)).toBeVisible()
    await expect(
      canvas.getByText(/The Earth is flat/i, {
        selector: '.graph-node-card__title-text',
      }),
    ).toBeVisible()
  },
}

export const Unselected: Story = {
  args: {
    nodes: flowGraph.nodes,
    edges: flowGraph.edges,
    selectedNodeId: undefined,
    onNodeSelect: () => undefined,
  },
}

export const WithSelectedNode: Story = {
  args: {
    nodes: flowGraph.nodes,
    edges: flowGraph.edges,
    selectedNodeId: 'C1',
    onNodeSelect: () => undefined,
  },
}
