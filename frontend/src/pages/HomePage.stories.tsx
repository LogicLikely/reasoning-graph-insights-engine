import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { HomePage } from './HomePage'

const meta = {
  component: HomePage,
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof HomePage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Map how reasoning structures hold together under pressure/i)).toBeVisible()
  },
}
