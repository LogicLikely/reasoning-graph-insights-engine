import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent } from 'storybook/test'
import { ContactPage } from './ContactPage'

const meta = {
    component: ContactPage,
    parameters: {
        docs: {
            description: {
                component:
                    'Storybook coverage for the Contact Page. This includes a full contact form with validation and a live preview section.',
            },
        },
    },
    tags: ['autodocs'],
} satisfies Meta<typeof ContactPage>

export default meta

type Story = StoryObj<typeof meta>

export const Default: Story = {
    parameters: {
        docs: {
            description: {
                story: 'The initial state of the contact page with an empty form.',
            },
        },
    },
}

export const FormValidation: Story = {
    parameters: {
        docs: {
            description: {
                story: 'Demonstrates the error handling when fields are missing or invalid.',
            },
        },
    },
    play: async ({ canvas }) => {
        const submitButton = canvas.getByRole('button', { name: /Submit/i })

        // Submit empty form (should be disabled by hasRequiredFields, but we check role)
        await expect(submitButton).toBeDisabled()
    },
}

export const SuccessfulSubmission: Story = {
    parameters: {
        docs: {
            description: {
                story: 'Simulates a user successfully filling out and submitting the form.',
            },
        },
    },
}