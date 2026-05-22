import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { GraphOverviewPanel } from './GraphOverviewPanel'

const meta = {
  component: GraphOverviewPanel,
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof GraphOverviewPanel>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  args: {
    title: 'Sample Reasoning Graph',
    description: 'A quick overview of the demo fixture and graph structure.',
    nodeCount: 11,
    edgeCount: 10,
    fixtureName: 'sample-medium',
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Graph Overview/i)).toBeVisible()
  },
}

export const SummaryOnly: Story = {
  args: {
    title: 'Another test graph',
    description: 'A shorter summary for a different demo fixture.',
    nodeCount: 5,
    edgeCount: 4,
    fixtureName: 'sample-small',
  },
}

export const FixtureLabel: Story = {
  args: {
    title: 'Graph with labels',
    description: 'This panel should show counts and fixture metadata clearly.',
    nodeCount: 8,
    edgeCount: 7,
    fixtureName: 'sample-labels',
  },
}
