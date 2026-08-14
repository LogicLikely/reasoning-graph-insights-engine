import { Given, Then, When } from '@cucumber/cucumber'
import { expect } from 'playwright/test'
import type { StorybookWorld } from '../support/world.js'

const GRAPH_TITLE = 'The Earth is flat'
const STORY_IDS = {
  selectedNodeDetails: 'components-graph-graphdetailspanel--default',
  emptyNodeDetails: 'components-graph-graphdetailspanel--empty',
  graphWorkspace: 'pages-demopage--default',
  loadingWorkspace: 'pages-demopage--loading-state',
  retryWorkspace: 'pages-demopage--retry-flow',
  graphSummary: 'components-graph-graphoverviewpanel--default',
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

Given('a graph node is selected', async function (this: StorybookWorld) {
  await ensureStorybookIsAvailable(this)
  this.currentStoryId = STORY_IDS.selectedNodeDetails
})

Given('no graph node is selected', async function (this: StorybookWorld) {
  await ensureStorybookIsAvailable(this)
  this.currentStoryId = STORY_IDS.emptyNodeDetails
})

Given('a graph workspace is available', async function (this: StorybookWorld) {
  await ensureStorybookIsAvailable(this)
  this.currentStoryId = STORY_IDS.graphWorkspace
})

Given('the graph is loading', async function (this: StorybookWorld) {
  await ensureStorybookIsAvailable(this)
  this.currentStoryId = STORY_IDS.loadingWorkspace
})

Given('the graph fails to load initially', async function (this: StorybookWorld) {
  await ensureStorybookIsAvailable(this)
  this.currentStoryId = STORY_IDS.retryWorkspace
})

Given('a graph summary is available', async function (this: StorybookWorld) {
  await ensureStorybookIsAvailable(this)
  this.currentStoryId = STORY_IDS.graphSummary
})

When('I view the graph details panel', async function (this: StorybookWorld) {
  await openCurrentStory(this)
})

When('I view the graph workspace', async function (this: StorybookWorld) {
  await openCurrentStory(this)
})

When('I view the graph summary', async function (this: StorybookWorld) {
  await openCurrentStory(this)
})

When('I retry loading the graph', async function (this: StorybookWorld) {
  await this.storyPage.getByRole('button', { name: /retry/i }).click()
})

Then('I should see the selected node title', async function (this: StorybookWorld) {
  await expect(
    this.storyPage.getByRole('heading', { name: GRAPH_TITLE }),
  ).toBeVisible()
})

Then('I should see guidance to select a node', async function (this: StorybookWorld) {
  await expect(
    this.storyPage.getByRole('heading', { name: 'Select a node to view details' }),
  ).toBeVisible()
})

Then('I should see the graph workspace title', async function (this: StorybookWorld) {
  await expect(
    this.storyPage.getByRole('heading', { level: 2, name: 'Sample Reasoning Graph' }),
  ).toBeVisible()
})

Then('the standard graph view should be selected', async function (this: StorybookWorld) {
  await expect(this.storyPage.getByRole('button', { name: 'Standard' })).toHaveAttribute(
    'aria-pressed',
    'true',
  )
  await expect(this.storyPage.getByTestId('graph-canvas')).toBeVisible()
})

When('I switch to the compact graph view', async function (this: StorybookWorld) {
  await this.storyPage.getByRole('button', { name: 'Compact' }).click()
})

Then('I should see the compact graph canvas', async function (this: StorybookWorld) {
  await expect(this.storyPage.getByTestId('insights-graph-canvas')).toBeVisible()
  await expect(this.storyPage.getByTestId('graph-canvas')).toHaveCount(0)
})

When('I expand the compact graph to the viewport', async function (this: StorybookWorld) {
  await this.storyPage.getByRole('button', { name: 'Expand graph to viewport' }).click()
})

Then('the compact graph should fill the viewport', async function (this: StorybookWorld) {
  const viewport = this.storyPage.viewportSize()
  const compactCanvas = await this.storyPage.getByTestId('insights-graph-canvas').boundingBox()

  expect(viewport).not.toBeNull()
  expect(compactCanvas).not.toBeNull()
  expect(Math.abs((compactCanvas?.width ?? 0) - (viewport?.width ?? 0))).toBeLessThanOrEqual(2)
  expect(Math.abs((compactCanvas?.height ?? 0) - (viewport?.height ?? 0))).toBeLessThanOrEqual(2)
})

When('I restore the compact graph size', async function (this: StorybookWorld) {
  await this.storyPage.getByRole('button', { name: 'Restore graph size' }).click()
})

When('I switch to the standard graph view', async function (this: StorybookWorld) {
  await this.storyPage.getByRole('button', { name: 'Standard' }).click()
})

Then('I should see the standard graph canvas', async function (this: StorybookWorld) {
  await expect(this.storyPage.getByTestId('graph-canvas')).toBeVisible()
  await expect(this.storyPage.getByTestId('insights-graph-canvas')).toHaveCount(0)
})

Then('standard support and rebut edges should retain different colors', async function (this: StorybookWorld) {
  const supportEdge = this.storyPage.locator('.graph-edge--support .react-flow__edge-path').first()
  const rebutEdge = this.storyPage.locator('.graph-edge--rebut .react-flow__edge-path').first()
  await expect(supportEdge).toBeVisible()
  await expect(rebutEdge).toBeVisible()

  const supportStroke = await supportEdge.evaluate((element) => getComputedStyle(element).stroke)
  const rebutStroke = await rebutEdge.evaluate((element) => getComputedStyle(element).stroke)

  expect(supportStroke).not.toBe(rebutStroke)
})

Then('I should see that the graph is loading', async function (this: StorybookWorld) {
  await expect(
    this.storyPage.getByRole('heading', { level: 2, name: 'Loading graph demo' }),
  ).toBeVisible()
  await expect(this.storyPage.getByText(/Fetching the current reasoning graph/i)).toBeVisible()
})

Then('I should be able to continue into the graph workspace', async function (this: StorybookWorld) {
  await expect(
    this.storyPage.getByRole('heading', { level: 2, name: 'Sample Reasoning Graph' }),
  ).toBeVisible()
  await expect(this.storyPage.getByTestId('graph-canvas')).toBeVisible()
})

Then('I should see the graph summary counts', async function (this: StorybookWorld) {
  await expect(this.storyPage.getByText('11')).toBeVisible()
  await expect(this.storyPage.getByText('10')).toBeVisible()
  await expect(this.storyPage.getByText('Nodes')).toBeVisible()
  await expect(this.storyPage.getByText('Edges')).toBeVisible()
  await expect(this.storyPage.getByText('sample-medium')).toBeVisible()
})
