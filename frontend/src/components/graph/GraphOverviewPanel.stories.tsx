import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { GraphOverviewPanel } from './GraphOverviewPanel'

const meta = {
  component: GraphOverviewPanel,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the graph overview sidebar. These stories exercise the summary card with different graph titles, counts, and fixture labels so the metadata presentation can be reviewed in isolation.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof GraphOverviewPanel>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Renders the standard overview panel content for the main sample graph. The interaction check confirms that the overview heading is visible and the panel is mounted normally.',
      },
    },
  },
  args: {
    title: 'Sample Reasoning Graph',
    description: 'A quick overview of the demo fixture and graph structure.',
    nodeCount: 11,
    edgeCount: 10,
    fixtureName: 'sample-medium',
    dataSource: 'fixture',
    renderer: 'standard',
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Graph Overview/i)).toBeVisible()
  },
}

export const SummaryOnly: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Shows a smaller graph summary with alternate counts and copy. This gives a quick visual check that the component scales to different metadata values.',
      },
    },
  },
  args: {
    title: 'Another test graph',
    description: 'A shorter summary for a different demo fixture.',
    nodeCount: 5,
    edgeCount: 4,
    fixtureName: 'sample-small',
    dataSource: 'database',
    renderer: 'compact',
  },
}

export const FixtureLabel: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Highlights the fixture label presentation by supplying a different fixture name and summary values. This is useful for checking that the metadata block stays readable across variants.',
      },
    },
  },
  args: {
    title: 'Graph with labels',
    description: 'This panel should show counts and fixture metadata clearly.',
    nodeCount: 8,
    edgeCount: 7,
    fixtureName: 'sample-labels',
    dataSource: 'fixture',
    renderer: 'standard',
  },
}
