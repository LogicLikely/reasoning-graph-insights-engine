import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { GraphDetailsPanel } from './GraphDetailsPanel'
import { sampleGraph } from '../../fixtures/sampleGraph'

const meta = {
  component: GraphDetailsPanel,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the graph details sidebar. These stories show how the panel behaves both when a node is selected and when the inspector is empty.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof GraphDetailsPanel>

export default meta

type Story = StoryObj<typeof meta>

const sampleNode = sampleGraph.nodes[0]

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Renders the details panel with a sample fixture node selected. The test verifies that the node title is visible so the primary inspector content is mounted.',
      },
    },
  },
  args: { node: sampleNode },
  play: async ({ canvas }) => {
    await expect(canvas.getByText(sampleNode.title)).toBeVisible()
  },
}

export const Empty: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Shows the empty-state panel when no node has been selected. This is the baseline guidance state users see before interacting with the graph.',
      },
    },
  },
  args: { node: undefined },
}

export const CssCheck: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'A lightweight structural check that confirms the expected root CSS class is present on the panel container. This helps catch accidental markup or class-name regressions.',
      },
    },
  },
  args: { node: sampleNode },
  play: async ({ canvas }) => {
    await expect(canvas.getByTestId('graph-details-panel')).toHaveClass('graph-details-panel')
  },
}
