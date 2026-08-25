import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, userEvent } from 'storybook/test'
import { ContactPage } from './ContactPage'

const meta = {
    component: ContactPage,
    parameters: {
        docs: {
            description: {
                component:
                    'Storybook coverage for the Contact Page. The page directs real messages to LogicLikely while preserving the original React form exercise, validation, console logging, and live preview.',
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
                story: 'The contact bridge, exercise disclaimer, and initial empty form.',
            },
        },
    },
    play: async ({ canvas }) => {
        await expect(canvas.getByText(/This form was an early onboarding exercise/i)).toBeVisible()
        await expect(canvas.getByRole('link', { name: /Contact LogicLikely/i })).toHaveAttribute(
            'href',
            'https://www.logiclikely.com/contact',
        )
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

        // Try to submit an empty form
        await userEvent.click(submitButton)

        // Expect validation error
        await expect(await canvas.findByText((c) => c.includes('Name is required'))).toBeVisible()
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
