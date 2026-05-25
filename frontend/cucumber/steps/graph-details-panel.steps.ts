import { Given, Then, When } from '@cucumber/cucumber'
import { expect } from 'playwright/test'
import type { StorybookWorld } from '../support/world.js'

const GRAPH_DETAILS_PANEL_STORY_ID = 'components-graph-graphdetailspanel--default'
const GRAPH_TITLE = 'The Earth is flat'

Given('Storybook is running for the frontend', async function (this: StorybookWorld) {
  const response = await this.storyPage.goto(this.baseUrl, {
    waitUntil: 'domcontentloaded',
  })

  if (!response?.ok()) {
    throw new Error(
      `Storybook did not respond successfully at ${this.baseUrl}. Start it with "npm run storybook" in the frontend directory.`,
    )
  }
})

When('I open the GraphDetailsPanel default story', async function (this: StorybookWorld) {
  await this.storyPage.goto(
    `${this.baseUrl}/iframe.html?id=${GRAPH_DETAILS_PANEL_STORY_ID}`,
    { waitUntil: 'domcontentloaded' },
  )
})

Then('I should see the selected node title', async function (this: StorybookWorld) {
  await expect(
    this.storyPage.getByRole('heading', { name: GRAPH_TITLE }),
  ).toBeVisible()
})
