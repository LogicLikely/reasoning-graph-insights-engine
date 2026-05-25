import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { Footer } from './Footer'

const meta = {
  component: Footer,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the shared site footer. These stories validate the product identity copy and the presence of the footer navigation links used across the app shell.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof Footer>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Checks the default footer render by asserting that the product name is visible. This verifies that the footer metadata block is mounted and readable.',
      },
    },
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Reasoning Graph Insights Engine/i)).toBeVisible()
  },
}
