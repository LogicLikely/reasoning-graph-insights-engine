import type { Meta, StoryObj } from '@storybook/react-vite';
import { expect, userEvent, fn } from 'storybook/test';
import { ContactForm } from './ContactPage';

type ContactFormValues = {
    name: string;
    email: string;
    topic: string;
    message: string;
};

const meta = {
    component: ContactForm,
    title: 'Contact/ContactForm',
    parameters: {
        docs: {
            description: {
                component: 'Storybook coverage for the Contact Form component in isolation. This includes various states like validation, submission, and initial values.',
            },
        },
    },
    tags: ['autodocs'],
} satisfies Meta<typeof ContactForm>;

export default meta;

type Story = StoryObj<typeof meta>;
type StoryPlayContext = Parameters<NonNullable<Story['play']>>[0];

async function fillContactForm(canvas: StoryPlayContext['canvas'], values: ContactFormValues) {
    await userEvent.type(canvas.getByLabelText(/Name/i), values.name);
    await userEvent.type(canvas.getByLabelText(/Email/i), values.email);
    await userEvent.selectOptions(canvas.getByLabelText(/Topic/i), values.topic);
    await userEvent.type(canvas.getByLabelText(/Message/i), values.message);
}

export const Default: Story = {
    parameters: {
        docs: {
            description: {
                story: 'The initial state of the contact form with all fields empty.',
            },
        },
    },
};

export const WithInitialValues: Story = {
    args: {
        initialName: 'Jane Doe',
        initialEmail: 'jane.doe@example.com',
        initialTopic: 'idea',
        initialMessage: 'Just an idea for a new feature!',
    },
    parameters: {
        docs: {
            description: {
                story: 'A form pre-filled with valid initial values.',
            },
        },
    },
    play: async ({ canvas }) => {
        await expect(canvas.getByLabelText(/Name/i)).toHaveValue('Jane Doe');
        await expect(canvas.getByLabelText(/Email/i)).toHaveValue('jane.doe@example.com');
        await expect(canvas.getByLabelText(/Topic/i)).toHaveValue('idea');
        await expect(canvas.getByLabelText(/Message/i)).toHaveValue('Just an idea for a new feature!');
    },
};

export const RequiredFieldValidation: Story = {
    parameters: {
        docs: {
            description: {
                story: 'Demonstrates validation messages for missing required fields.',
            },
        },
    },
    play: async ({ canvas }) => {
        const submitButton = canvas.getByRole('button', { name: /Submit/i });

        // Try to submit an empty form
        await userEvent.click(submitButton);

        // Expect error messages for each field, one by one as they are filled
        await expect(await canvas.findByText((c) => c.includes('Name is required'))).toBeVisible();

        // Fill name, submit, expect email error
        await userEvent.type(canvas.getByLabelText(/Name/i), 'Test User');
        await userEvent.click(submitButton);
        await expect(await canvas.findByText((c) => c.includes('Invalid email format'))).toBeVisible();

        // Fill email, submit, expect topic error
        await userEvent.type(canvas.getByLabelText(/Email/i), 'test@example.com');
        await userEvent.click(submitButton);
        await expect(await canvas.findByText((c) => c.includes('Topic is required'))).toBeVisible();

        // Fill topic, submit, expect message error
        await userEvent.selectOptions(canvas.getByLabelText(/Topic/i), 'question');
        await userEvent.click(submitButton);
        await expect(await canvas.findByText((c) => c.includes('Message is required'))).toBeVisible();
    },
};

export const InvalidEmailValidation: Story = {
    args: {
        initialName: 'Test User',
        initialEmail: 'invalid-email', // Invalid email format
        initialTopic: 'bug',
        initialMessage: 'This is a test message.',
    },
    parameters: {
        docs: {
            description: {
                story: 'Demonstrates the validation message for an invalid email format.',
            },
        },
    },
    play: async ({ canvas }) => {
        const submitButton = canvas.getByRole('button', { name: /Submit/i });

        // Submit with invalid email
        await userEvent.click(submitButton);

        // Expect invalid email error message
        await expect(await canvas.findByText((c) => c.includes('Invalid email format'))).toBeVisible();
    },
};

export const SubmittingState: Story = {
    args: {
        initialName: 'Loading User',
        initialEmail: 'loading@example.com',
        initialTopic: 'question',
        initialMessage: 'Testing the submitting state.',
        onSubmit: fn(async (submission) => {
            console.log("Simulating long submission:", submission);
            await new Promise((resolve) => setTimeout(resolve, 500)); // Simulate a long API call
            return { success: true, message: "Submission complete!" };
        }),
    },
    parameters: {
        docs: {
            description: {
                story: 'Demonstrates the form while it is in the process of submitting data.',
            },
        },
    },
    play: async ({ canvas }) => {
        const submitButton = canvas.getByRole('button', { name: /Submit/i });

        // Click submit
        await userEvent.click(submitButton);

        // Expect button to show "Submitting..." and be disabled
        await expect(submitButton).toHaveTextContent('Submitting...');
        await expect(submitButton).toBeDisabled();

        // Wait for submission to complete and check for success message
        await expect(await canvas.findByText((c) => c.includes('Submission complete!'))).toBeVisible();
        await expect(submitButton).toHaveTextContent('Submit'); // Button text should revert
        await expect(submitButton).toBeEnabled(); // Button should be enabled again
    },
};

export const SuccessfulSubmission: Story = {
    args: {
        initialName: 'Success User',
        initialEmail: 'success@example.com',
        initialTopic: 'idea',
        initialMessage: 'This is a successful submission test.',
        onSubmit: fn(async (submission) => {
            console.log("Mock successful submission:", submission);
            return { success: true, message: "Contact form submitted successfully!" };
        }),
    },
    parameters: {
        docs: {
            description: {
                story: 'Demonstrates the form after a successful submission, showing the success message and cleared fields.',
            },
        },
    },
    play: async ({ canvas, args }) => {
        const submitButton = canvas.getByRole('button', { name: /Submit/i });

        // Click submit
        await userEvent.click(submitButton);

        // Expect success message
        await expect(await canvas.findByText((c) => c.includes('Contact form submitted successfully!'))).toBeVisible();
        await expect(args.onSubmit).toHaveBeenCalledWith({
            name: 'Success User',
            email: 'success@example.com',
            topic: 'idea',
            message: 'This is a successful submission test.',
        });
        // Form fields should be cleared
        await expect(canvas.getByLabelText(/Name/i)).toHaveValue('');
        await expect(canvas.getByLabelText(/Email/i)).toHaveValue('');
        await expect(canvas.getByLabelText(/Topic/i)).toHaveValue('');
        await expect(canvas.getByLabelText(/Message/i)).toHaveValue('');
    },
};

export const FailedSubmission: Story = {
    args: {
        initialName: 'Error User',
        initialEmail: 'error@example.com',
        initialTopic: 'bug',
        initialMessage: 'This is a failed submission test.',
        onSubmit: fn(async (submission) => {
            console.log("Mock failed submission:", submission);
            return { success: false, message: "Failed to submit form. Please try again." };
        }),
    },
    parameters: {
        docs: {
            description: {
                story: 'Demonstrates the form after a failed submission, showing an error message and retaining field values.',
            },
        },
    },
    play: async ({ canvas, args }) => {
        const submitButton = canvas.getByRole('button', { name: /Submit/i });

        // Click submit
        await userEvent.click(submitButton);

        // Expect error message
        await expect(await canvas.findByText((c) => c.includes('Failed to submit form'))).toBeVisible();
        await expect(args.onSubmit).toHaveBeenCalledWith({
            name: 'Error User',
            email: 'error@example.com',
            topic: 'bug',
            message: 'This is a failed submission test.',
        });
        // Form fields should NOT be cleared on failure
        await expect(canvas.getByLabelText(/Name/i)).toHaveValue('Error User');
        await expect(canvas.getByLabelText(/Email/i)).toHaveValue('error@example.com');
    },
};

export const FilledOut: Story = {
    parameters: {
        docs: {
            description: {
                story: 'Simulates a user manually filling out every field in the form to verify that the input logic and live preview function as expected.',
            },
        },
    },
    play: async ({ canvas }) => {
        await fillContactForm(canvas, {
            name: 'Jacob',
            email: 'jacob@example.com',
            topic: 'question',
            message: 'This is a test message.',
        });

        // Verify the form fields and the preview section are in sync
        await expect(canvas.getByLabelText(/Name/i)).toHaveValue('Jacob');
        await expect(canvas.getByText(/jacob@example.com/i)).toBeInTheDocument();
    },
};
