import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  BROWSER_JOURNEY_COMPLETE_EVENT,
  BROWSER_JOURNEY_PHASE_EVENT,
  type BrowserJourneyConfig,
} from './browserJourneyContract'
import { InsightsBrowserPerformanceHarness } from './InsightsBrowserPerformanceHarness'
import { observeGraphResourceTiming } from './graphResourceTiming'

const config: BrowserJourneyConfig = {
  runId: '11111111-1111-1111-1111-111111111111',
  sampleId: '22222222-2222-2222-2222-222222222222',
  scenarioId: 'browser.result.strongest-path.quick',
  correlationId: 'test-correlation',
  action: 'result-render',
  graphSlug: 'stress-balanced-1k',
  apiBaseUrl: null,
  searchQuery: null,
}

describe('InsightsBrowserPerformanceHarness', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    delete window.__logiclikelyInsightsBenchmark
    delete window.__logiclikelyInsightsBenchmarkInput
  })

  it('emits incremental raw phases before a frozen-token terminal result', async () => {
    const phases: unknown[] = []
    const terminals: unknown[] = []
    window.addEventListener(BROWSER_JOURNEY_PHASE_EVENT, (event) => {
      phases.push((event as CustomEvent).detail)
    }, { once: false })
    window.addEventListener(BROWSER_JOURNEY_COMPLETE_EVENT, (event) => {
      terminals.push((event as CustomEvent).detail)
    }, { once: true })

    render(
      <InsightsBrowserPerformanceHarness
        config={config}
        resultPayload={{
          operationId: 'strongest-path-v1',
          status: 'succeeded',
          totalResultCardinality: 2,
          resultDigest: 'sha256:result',
          items: [{ rank: 1 }, { rank: 2 }],
          orderedPaths: [{ pathId: 'p1', nodeIds: ['n-0', 'n-1'] }],
        }}
      />,
    )

    await waitFor(() => {
      expect(screen.getByTestId('insights-browser-benchmark-result')).toHaveAttribute(
        'data-state',
        'completed',
      )
    })

    const state = window.__logiclikelyInsightsBenchmark
    expect(state?.state).toBe('completed')
    expect(state?.result).toMatchObject({
      status: 'succeeded',
      totalResultCardinality: 2,
      boundedResultItemCount: 2,
      driverPayload: null,
      matchNodeIds: null,
      exactSuppressions: [],
      evidence: {
        harnessBuildIdentity: 'unconfigured',
        nextHopProtocol: null,
        resourceTimingLimitation: expect.stringContaining('no graph REST request'),
      },
    })
    expect(state?.result?.phases.map(({ layer, phase }) => `${layer}/${phase}`)).toEqual([
      'lab-result/react-commit',
      'lab-result/result-render',
    ])
    expect(phases).toHaveLength(2)
    expect(terminals).toHaveLength(1)
    for (const phase of state?.result?.phases ?? []) {
      expect(phase.evidence).toMatchObject({
        startMilliseconds: expect.any(Number),
        endMilliseconds: expect.any(Number),
        sequence: expect.any(Number),
      })
      expect(Number(phase.evidence.endMilliseconds)).toBeGreaterThanOrEqual(
        Number(phase.evidence.startMilliseconds),
      )
    }
  })

  it('reports only the network protocol observed for the exact resource URL', () => {
    const url = 'http://127.0.0.1:5010/api/graphs/stress-balanced-1k'
    vi.spyOn(performance, 'getEntriesByName').mockReturnValue([{
      name: url,
      entryType: 'resource',
      startTime: 12,
      duration: 34,
      nextHopProtocol: 'h2',
      transferSize: 456,
      encodedBodySize: 400,
      decodedBodySize: 900,
    } as PerformanceResourceTiming])

    expect(observeGraphResourceTiming(url)).toEqual({
      nextHopProtocol: 'h2',
      resourceTimingLimitation: null,
      resourceTiming: {
        name: url,
        entryCount: 1,
        startTimeMilliseconds: 12,
        durationMilliseconds: 34,
        transferSizeBytes: 456,
        encodedBodySizeBytes: 400,
        decodedBodySizeBytes: 900,
      },
    })
  })

  it('discloses when no exact PerformanceResourceTiming entry is available', () => {
    vi.spyOn(performance, 'getEntriesByName').mockReturnValue([])

    expect(observeGraphResourceTiming('http://127.0.0.1:5010/api/graphs/missing'))
      .toMatchObject({
        nextHopProtocol: null,
        resourceTimingLimitation: expect.stringContaining('no PerformanceResourceTiming entry'),
      })
  })
})
