import { Given, Then, When } from '@cucumber/cucumber'
import { expect } from 'playwright/test'
import type { StorybookWorld } from '../support/world.js'

const STORY_IDS = {
    contactForm: 'contact-contactform--default',
} as const

async function ensureStorybookIsAvailable(world: StorybookWorld) {
    const response = await world.storyPage.goto(world.baseUrl, {
        waitUntil: 'domcontentloaded',
    })

    if (!response?.ok()) {
        throw new Error(
            `Storybook did not respond successfully at ${world.baseUrl}. Start it with "npm run storybook" in the frontend directory.`,
        )
    }
}

async function openCurrentStory(world: StorybookWorld) {
    if (!world.currentStoryId) {
        throw new Error('No Storybook fixture has been selected for this scenario.')
    }

    await world.storyPage.goto(
        `${world.baseUrl}/iframe.html?id=${world.currentStoryId}`,
        { waitUntil: 'domcontentloaded' },
    )
}

Given('the contact form story is loaded', async function (this: StorybookWorld) {
    await ensureStorybookIsAvailable(this)
    this.currentStoryId = STORY_IDS.contactForm
    await openCurrentStory(this)
})

When('I click the submit button', async function (this: StorybookWorld) {
    await this.storyPage.getByRole('button', { name: /submit/i }).click()
})

Then('I should see the validation message {string}', async function (this: StorybookWorld, message: string) {
    await expect(this.storyPage.getByText(message)).toBeVisible()
})

Given('the contact form is empty', async function (this: StorybookWorld) {
    await ensureStorybookIsAvailable(this)
    this.currentStoryId = STORY_IDS.contactForm
    await openCurrentStory(this)
})

When('I try to submit the contact form', async function (this: StorybookWorld) {
    await this.storyPage.getByRole('button', { name: /submit/i }).click()
})

Then('I should see that name is required', async function (this: StorybookWorld) {
    await expect(this.storyPage.getByText(/Name is required/i)).toBeVisible()
})

Given('the contact form has an invalid email address', async function (this: StorybookWorld) {
    await ensureStorybookIsAvailable(this)
    this.currentStoryId = STORY_IDS.contactForm
    await openCurrentStory(this)
    await this.storyPage.getByLabel(/Name/i).fill('Test User')
    await this.storyPage.getByLabel(/Email/i).fill('invalid-email')
    await this.storyPage.getByLabel(/Topic/i).selectOption('question')
    await this.storyPage.getByLabel(/Message/i).fill('I have a question.')
})

Then('I should see that the email address is invalid', async function (this: StorybookWorld) {
    await expect(this.storyPage.getByText(/Invalid email format/i)).toBeVisible()
})

Given('the contact form has valid values', async function (this: StorybookWorld) {
    await ensureStorybookIsAvailable(this)
    this.currentStoryId = STORY_IDS.contactForm
    await openCurrentStory(this)
    await this.storyPage.getByLabel(/Name/i).fill('Jacob')
    await this.storyPage.getByLabel(/Email/i).fill('jacob@example.com')
    await this.storyPage.getByLabel(/Topic/i).selectOption('idea')
    await this.storyPage.getByLabel(/Message/i).fill('This is a valid submission test.')
})

When('I submit the contact form', async function (this: StorybookWorld) {
    await this.storyPage.getByRole('button', { name: /submit/i }).click()
})

Then('I should see a successful submission message', async function (this: StorybookWorld) {
    await expect(this.storyPage.getByText(/Contact form submitted/i)).toBeVisible()
})

Then('I should not be able to submit the contact form', async function (this: StorybookWorld) {
    await expect(this.storyPage.getByRole('button', { name: /submit/i })).toBeDisabled()
})
