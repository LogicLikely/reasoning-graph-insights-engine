import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { Footer } from './Footer'

const meta = {
  component: Footer,
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof Footer>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Reasoning Graph Insights Engine/i)).toBeVisible()
  },
}
