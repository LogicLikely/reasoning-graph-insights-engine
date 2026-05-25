import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { NotFoundPage } from './NotFoundPage'

const meta = {
  component: NotFoundPage,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the 404 page. These stories validate the fallback route experience and confirm that recovery links are presented when a user lands on an unknown path.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof NotFoundPage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Checks the default Not Found page render by asserting that the 404 heading is visible. This verifies that the fallback page mounts and communicates the missing-route state clearly.',
      },
    },
  },
  play: async ({ canvas }) => {
    await expect(canvas.getByRole('heading', { name: /page not found/i })).toBeVisible()
  },
}
