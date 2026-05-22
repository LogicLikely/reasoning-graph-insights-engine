import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { GraphDetailsPanel } from './GraphDetailsPanel'
import { sampleGraph } from '../../fixtures/sampleGraph'

const meta = {
  component: GraphDetailsPanel,
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof GraphDetailsPanel>

export default meta

type Story = StoryObj<typeof meta>

const sampleNode = sampleGraph.nodes[0]

export const Default: Story = {
  args: { node: sampleNode },
  play: async ({ canvas }) => {
    await expect(canvas.getByText(sampleNode.title)).toBeVisible()
  },
}

export const Empty: Story = {
  args: { node: undefined },
}

export const CssCheck: Story = {
  args: { node: sampleNode },
  play: async ({ canvas }) => {
    await expect(canvas.getByTestId('graph-details-panel')).toHaveClass('graph-details-panel')
  },
}
