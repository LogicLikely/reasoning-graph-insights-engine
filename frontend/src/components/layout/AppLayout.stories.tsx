import { Route, Routes } from 'react-router-dom'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect } from 'storybook/test'
import { AppLayout } from './AppLayout'

const meta = {
  component: AppLayout,
  tags: ['ai-generated', 'needs-work'],
} satisfies Meta<typeof AppLayout>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
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
