import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { HomePage } from './HomePage'

const meta = {
  component: HomePage,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the Home page. These stories focus on the landing-page messaging and confirm that the entry-point content for the product shell renders as expected.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof HomePage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Checks the default Home page render by asserting that the main value-proposition headline is visible. This verifies that the primary landing-page hero content loads correctly.',
      },
    },
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Map how reasoning structures hold together under pressure/i)).toBeVisible()
    await expect(
      canvas.getByRole('link', { name: /Explore the project/i }),
    ).toHaveAttribute('href', '/about')
  },
}
