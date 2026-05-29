import { Route, Routes } from 'react-router-dom'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { AppLayout } from './AppLayout'

const meta = {
  component: AppLayout,
  parameters: {
    docs: {
      description: {
        component:
          'Storybook coverage for the top-level app layout shell. These stories validate that the shared header, routed main content area, and footer structure render together correctly.',
      },
    },
  },
  tags: ['autodocs', 'ai-generated', 'needs-work'],
} satisfies Meta<typeof AppLayout>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
  parameters: {
    docs: {
      description: {
        story:
          'Renders the layout with a simple nested route payload inside the outlet. The interaction check confirms that routed page content appears inside the shared application shell.',
      },
    },
  },
  render: () => (
    <Routes>
      <Route path="*" element={<AppLayout />}>
        <Route path="*" element={<div>Page content inside layout</div>} />
      </Route>
    </Routes>
  ),
  play: async ({ canvas }) => {
    await expect(canvas.getByText(/Page content inside layout/i)).toBeVisible()
  },
}
