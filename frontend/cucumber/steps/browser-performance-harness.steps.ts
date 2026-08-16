import { Given, Then, When } from '@cucumber/cucumber'
import { expect } from 'playwright/test'
import type { StorybookWorld } from '../support/world.js'

const STORY_ID = 'performance-insightsbrowserharness--journey'
const RUN_ID = '11111111-1111-1111-1111-111111111111'
const SAMPLE_ID = '22222222-2222-2222-2222-222222222222'

Given('a bounded browser performance result fixture is available', async function (
  this: StorybookWorld,
) {
  const response = await this.storyPage.goto(this.baseUrl, { waitUntil: 'domcontentloaded' })
  if (!response?.ok()) {
    throw new Error('Storybook is not available for the browser performance fixture.')
  }

  await this.storyPage.addInitScript((input) => {
    const benchmarkWindow = globalThis as typeof globalThis & {
      __logiclikelyInsightsBenchmarkInput?: unknown
    }
    benchmarkWindow.__logiclikelyInsightsBenchmarkInput = input
  }, {
    runId: RUN_ID,
    sampleId: SAMPLE_ID,
    scenarioId: 'browser.result.strongest-path.functional',
    action: 'result-render',
    resultPayload: {
      operationId: 'strongest-path-v1',
      status: 'succeeded',
      totalResultCardinality: 125,
      resultDigest: 'sha256:functional-result',
      summary: { completePathCount: 125 },
      items: Array.from({ length: 125 }, (_, index) => ({
        rank: index + 1,
        terminalNodeId: `n-${String(index).padStart(5, '0')}`,
      })),
      orderedPaths: [{
        pathId: 'representative-path',
        nodeIds: ['n-00000', 'n-00001', 'n-00042'],
      }],
    },
  })
  this.currentStoryId = STORY_ID
})

When('I run the bounded result browser journey', async function (this: StorybookWorld) {
  await this.storyPage.goto(
    `${this.baseUrl}/iframe.html?id=${this.currentStoryId}` +
    `&benchmarkRunId=${RUN_ID}&benchmarkSampleId=${SAMPLE_ID}` +
    '&benchmarkAction=result-render',
    { waitUntil: 'domcontentloaded' },
  )
  await expect(this.storyPage.getByTestId('insights-browser-benchmark-result')).toHaveAttribute(
    'data-state',
    'completed',
  )
})

Then('the browser performance journey should succeed', async function (this: StorybookWorld) {
  const result = await this.storyPage.evaluate(() => (
    (globalThis as typeof globalThis & {
      __logiclikelyInsightsBenchmark?: { result?: { status?: string } }
    }).__logiclikelyInsightsBenchmark?.result
  ))
  expect(result?.status).toBe('succeeded')
})

Then('it should expose incremental result-render timing evidence', async function (
  this: StorybookWorld,
) {
  const phases = await this.storyPage.evaluate(() => (
    (globalThis as typeof globalThis & {
      __logiclikelyInsightsBenchmark?: {
        result?: { phases?: Array<{ layer: string; phase: string; evidence: unknown }> }
      }
    }).__logiclikelyInsightsBenchmark?.result?.phases ?? []
  ))
  expect(phases.map(({ layer, phase }) => `${layer}/${phase}`)).toEqual([
    'lab-result/react-commit',
    'lab-result/result-render',
  ])
  expect(phases.every(({ evidence }) => Boolean(evidence))).toBe(true)
})

Then('it should preserve complete cardinality while bounding mounted rows', async function (
  this: StorybookWorld,
) {
  await expect(this.storyPage.getByTestId('result-total-cardinality')).toHaveText('125')
  await expect(this.storyPage.getByTestId('bounded-result-item-count')).toHaveText(
    'Rendering 100 of 125 result items.',
  )
  await expect(this.storyPage.getByRole('table').getByRole('row')).toHaveCount(101)
})
