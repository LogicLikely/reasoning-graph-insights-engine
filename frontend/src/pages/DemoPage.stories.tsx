import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, mocked, userEvent } from 'storybook/test'
import { sampleGraph } from '../fixtures/sampleGraph'
import { getGraphBySlug } from '../services/graphService'
import { DemoPage } from './DemoPage'

const meta = {
  component: DemoPage,
  beforeEach: async () => {
    mocked(getGraphBySlug).mockReset()
    mocked(getGraphBySlug).mockResolvedValue(sampleGraph)
  },
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the GraphMap-backed demo page. These stories keep fixture-backed graph data enabled while mocking the graph service per story to demonstrate the success, loading, and retry states that users can encounter.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof DemoPage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Verifies the normal success path. The mocked graph service resolves with the sample fixture, and the play function confirms that the page intro and the loaded graph heading both appear.',
      },
    },
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Interactive Graph Demo/i)).toBeVisible()
    await expect(
      await canvas.findByRole('heading', { level: 2, name: /Sample Reasoning Graph/i })
    ).toBeVisible()
    await expect(await canvas.findByTestId('insights-graph-canvas')).toBeVisible()
  },
}

export const LoadingState: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Demonstrates the in-flight loading state. This story makes the mocked graph request never resolve so the page stays in its loading UI, then checks that the loading panel and placeholder heading are visible.',
      },
    },
  },
  beforeEach: async () => {
    mocked(getGraphBySlug).mockReset()
    mocked(getGraphBySlug).mockImplementation(() => new Promise(() => {}))
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByTestId('demo-loading-state')).toBeVisible()
    await expect(canvas.getByRole('heading', { level: 2, name: /Loading graph demo/i })).toBeVisible()
  },
}

export const RetryFlow: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Exercises the recovery path after a failed load. The first mocked request rejects to show the error state, the test pauses briefly so the failure is visible, then clicks Retry and confirms the second request succeeds and the graph renders.',
      },
    },
  },
  beforeEach: async () => {
    mocked(getGraphBySlug).mockReset()
    mocked(getGraphBySlug)
      .mockRejectedValueOnce(new Error('Request failed'))
      .mockResolvedValueOnce(sampleGraph)
  },
  play: async ({ canvas }) => {
    await expect(await canvas.findByTestId('demo-error-state')).toBeVisible()
    await new Promise((resolve) => window.setTimeout(resolve, 400))

    await userEvent.click(canvas.getByRole('button', { name: /retry/i }))

    await expect(
      await canvas.findByRole('heading', { level: 2, name: /Sample Reasoning Graph/i })
    ).toBeVisible()
    await expect(await canvas.findByTestId('insights-graph-canvas')).toBeVisible()
  },
}
