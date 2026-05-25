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
    docs: {
      description: {
        component:
          'Storybook coverage for the React Flow graph canvas. These stories render fixture-backed flow data so the canvas can be inspected in selected and unselected states without depending on the full demo page.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof GraphCanvas>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Renders the canvas with the fixture graph and a selected root node. The interaction check confirms that the React Flow shell is visible and that a node title from the mapped graph data is present.',
      },
    },
  },
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
  parameters: {
    docs: {
      description: {
        story:
          'Shows the same fixture graph with no selected node. This is useful for visually checking the neutral canvas state before a user clicks into any graph node.',
      },
    },
  },
  args: {
    nodes: flowGraph.nodes,
    edges: flowGraph.edges,
    selectedNodeId: undefined,
    onNodeSelect: () => undefined,
  },
}

export const WithSelectedNode: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Displays the graph with a supporting claim selected instead of the root node. This variant makes it easier to inspect selection styling on a different part of the graph.',
      },
    },
  },
  args: {
    nodes: flowGraph.nodes,
    edges: flowGraph.edges,
    selectedNodeId: 'C1',
    onNodeSelect: () => undefined,
  },
}
