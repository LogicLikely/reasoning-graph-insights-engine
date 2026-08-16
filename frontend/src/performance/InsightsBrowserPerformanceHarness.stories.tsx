import type { Meta, StoryObj } from '@storybook/react-vite'
import { expect, waitFor } from 'storybook/test'
import { BoundedAnalysisResult } from './BoundedAnalysisResult'
import { InsightsBrowserPerformanceHarness } from './InsightsBrowserPerformanceHarness'
import type { BoundedAnalysisResultPayload } from './browserJourneyContract'

const resultStateFixtures: BoundedAnalysisResultPayload[] = [
  {
    operationId: 'strongest-path-v1',
    status: 'succeeded',
    title: 'Strongest path',
    totalResultCardinality: 1,
    resultDigest: 'sha256:storybook-success',
    summary: { pathCount: 1, strategy: 'exact' },
    topItems: [{ rank: 1, score: 0.84, terminalNodeId: 'n-00042' }],
    orderedPaths: [{ pathId: 'path-1', nodeIds: ['n-00000', 'n-00010', 'n-00042'], score: 0.84 }],
  },
  {
    operationId: 'critical-counter-v1',
    status: 'succeeded',
    title: 'Critical counter set',
    totalResultCardinality: 2,
    resultDigest: 'sha256:storybook-critical-counter-success',
    summary: { strategy: 'exact', selectedCounterCount: 2, thresholdReached: true },
    topItems: [
      { rank: 1, nodeId: 'counter-00007', marginalImpact: 0.31 },
      { rank: 2, nodeId: 'counter-00012', marginalImpact: 0.18 },
    ],
  },
  {
    operationId: 'evidence-impact-v0',
    status: 'succeeded',
    title: 'Evidence impact ranking',
    totalResultCardinality: 2,
    resultDigest: 'sha256:storybook-evidence-impact-success',
    summary: { supporting: 1, counter: 1 },
    distribution: [
      { label: 'supporting', value: 0.5 },
      { label: 'counter', value: 0.5 },
    ],
    topItems: [
      { rank: 1, nodeId: 'evidence-00004', impact: 0.27, kind: 'supporting' },
      { rank: 2, nodeId: 'evidence-00009', impact: -0.19, kind: 'counter' },
    ],
  },
  {
    operationId: 'robustness-v0',
    status: 'succeeded',
    title: 'Node robustness',
    totalResultCardinality: 2,
    resultDigest: 'sha256:storybook-robustness-success',
    summary: { leastRobustNodeId: 'n-00013', leastRobustness: 0.21 },
    distribution: [
      { label: 'low', value: 0.5 },
      { label: 'high', value: 0.5 },
    ],
    topItems: [
      { rank: 1, nodeId: 'n-00013', robustnessScore: 0.21 },
      { rank: 2, nodeId: 'n-00021', robustnessScore: 0.74 },
    ],
  },
  {
    operationId: 'evidence-impact-v0-empty',
    status: 'succeeded',
    title: 'Empty evidence result',
    totalResultCardinality: 0,
    resultDigest: 'sha256:storybook-empty',
    summary: { supporting: 0, counter: 0 },
  },
  ...(['failed', 'timed-out', 'cancelled', 'crashed', 'skipped'] as const).map((status) => ({
    operationId: 'critical-counter-v1',
    status,
    title: `Critical counter · ${status}`,
    totalResultCardinality: 0,
    resultDigest: `sha256:storybook-${status}`,
    summary: { retainedCardinality: 0 },
    failure: {
      kind: status,
      message: `Deterministic ${status} presentation fixture.`,
    },
  })),
]

function ResultStateFixtures() {
  return (
    <div style={{ display: 'grid', gap: '1rem', padding: '1rem', background: '#eef2e6' }}>
      {resultStateFixtures.map((payload) => (
        <BoundedAnalysisResult
          key={`${payload.operationId}-${payload.title}`}
          payload={payload}
        />
      ))}
    </div>
  )
}

const meta = {
  title: 'Performance/InsightsBrowserHarness',
  component: InsightsBrowserPerformanceHarness,
  parameters: {
    layout: 'fullscreen',
    docs: {
      description: {
        component:
          'Test-only Phase 4 browser measurement harness. It reuses the production GraphMap consumer and exposes incremental phase plus terminal JSON evidence without adding a product route.',
      },
    },
    msw: {
      handlers: {
        graphs: null,
      },
    },
  },
} satisfies Meta<typeof InsightsBrowserPerformanceHarness>

export default meta

type Story = StoryObj<typeof meta>

/**
 * Runner-controlled journey. Query parameters or the pre-navigation global
 * input select the real API base URL, graph, action, search, and result payload.
 */
export const Journey: Story = {
  play: async ({ canvas }) => {
    await waitFor(() => {
      expect(canvas.getByTestId('insights-browser-benchmark-result')).toHaveAttribute(
        'data-state',
        'completed',
      )
    }, { timeout: 30_000 })
  },
}

export const ResultStates: Story = {
  render: () => <ResultStateFixtures />,
  parameters: {
    docs: {
      description: {
        story:
          'Deterministic strongest-path, critical-counter, evidence-impact, robustness, empty, failed, timed-out, cancelled, crashed, and skipped presentation fixtures.',
      },
    },
  },
}
