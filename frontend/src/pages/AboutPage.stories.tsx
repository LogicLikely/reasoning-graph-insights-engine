import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { AboutPage } from './AboutPage'

const meta = {
  component: AboutPage,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the About page. These stories validate that the architecture and project-positioning copy renders correctly inside the app shell.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof AboutPage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Checks the default About page render by asserting that the main hero copy is visible. This verifies that the page-level explanatory content is mounted and readable.',
      },
    },
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/A small platform for reasoning graphs/i)).toBeVisible()
  },
}
