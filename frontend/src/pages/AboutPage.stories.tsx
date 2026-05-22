import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { AboutPage } from './AboutPage'

const meta = {
  component: AboutPage,
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof AboutPage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/A small platform for reasoning graphs/i)).toBeVisible()
  },
}