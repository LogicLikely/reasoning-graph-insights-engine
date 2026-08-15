import type { Meta, StoryObj } from '@storybook/react-vite'
import { fn } from 'storybook/test'
import { DatabaseResetDialog } from './DatabaseResetDialog'

const meta = {
  component: DatabaseResetDialog,
  parameters: {
    docs: {
      description: {
        component:
          'Accessible database-reset dialog for choosing optional 1K and 10K stress graphs. Standard example graphs are always restored.',
      },
    },
    layout: 'fullscreen',
  },
  tags: ['autodocs'],
  args: {
    isOpen: true,
    initialSelectedStressGraphIds: ['stress-balanced-1k', 'stress-shared-diamond-10k'],
    onCancel: fn(),
    onConfirm: fn(),
  },
} satisfies Meta<typeof DatabaseResetDialog>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {}

export const ResetFailure: Story = {
  args: {
    error: 'The database reset failed or could not be confirmed. The current view has been retained.',
  },
}

export const Resetting: Story = {
  args: {
    isSubmitting: true,
  },
}
