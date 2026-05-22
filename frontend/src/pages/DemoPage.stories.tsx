import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { DemoPage } from './DemoPage'

const meta = {
  component: DemoPage,
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof DemoPage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Interactive Graph Demo/i)).toBeVisible()
    await expect(
      canvas.getByRole('heading', { level: 2, name: /Sample Reasoning Graph/i })
    ).toBeVisible()
  },
}

export const LoadingState: Story = {
  args: {},
}

export const RetryFlow: Story = {
  args: {},
}
