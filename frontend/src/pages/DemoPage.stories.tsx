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
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof DemoPage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Interactive Graph Demo/i)).toBeVisible()
    await expect(
      await canvas.findByRole('heading', { level: 2, name: /Sample Reasoning Graph/i })
    ).toBeVisible()
  },
}

export const LoadingState: Story = {
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
    await expect(await canvas.findByTestId('graph-canvas')).toBeVisible()
  },
}
