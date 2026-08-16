import { describe, expect, it } from 'vitest'
import {
  DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS,
  GRAPH_MAP_RENDER_BUDGET_DECISIONS,
  GRAPH_MAP_RENDER_BUDGET_SOURCES,
  GRAPH_MAP_VIEW_LIFECYCLE_PHASES,
  classifyGraphMapRenderBudgetNodeCount,
  normalizeGraphMapRenderBudgetSettings,
  type GraphMapRenderBudgetEvent,
  type GraphMapRenderBudgetSource,
} from './graphMapRenderBudgetContract'

describe('GraphMap render-budget contract', () => {
  it.each([
    [999, 'allow'],
    [1_000, 'warn'],
    [1_200, 'warn'],
    [1_201, 'block'],
  ] as const)(
    'classifies %i materialized nodes as %s',
    (candidateGraphNodeCount, expectedDecision) => {
      const result = classifyGraphMapRenderBudgetNodeCount({
        candidateGraphNodeCount,
        candidateSyntheticNodeCount: 0,
      })

      expect(result.decision).toBe(expectedDecision)
      expect(result.candidateTotalNodeCount).toBe(candidateGraphNodeCount)
      expect(result.thresholds).toEqual({
        warnAtNodes: 1_000,
        blockAboveNodes: 1_200,
      })
    },
  )

  it('includes synthetic controls in the materialized-node total', () => {
    expect(
      classifyGraphMapRenderBudgetNodeCount({
        candidateGraphNodeCount: 999,
        candidateSyntheticNodeCount: 1,
      }),
    ).toMatchObject({
      decision: 'warn',
      candidateGraphNodeCount: 999,
      candidateSyntheticNodeCount: 1,
      candidateTotalNodeCount: 1_000,
    })

    expect(
      classifyGraphMapRenderBudgetNodeCount({
        candidateGraphNodeCount: 1_200,
        candidateSyntheticNodeCount: 1,
      }).decision,
    ).toBe('block')
  })

  it.each([
    ['tree-999', 'initial', 999, 0, 'allow'],
    ['shared-dag-1000-distinct', 'expand-all', 1_000, 0, 'warn'],
    ['cycle-safe-1200-distinct', 'search', 1_200, 0, 'warn'],
    ['synthetic-more-1201-total', 'show-more', 1_200, 1, 'block'],
    ['replacement-1201', 'graph-update', 1_201, 0, 'block'],
    ['controlled-view-1201', 'controlled-view', 1_201, 0, 'block'],
  ] as const)(
    'freezes the %s projection fixture',
    (
      _fixture,
      _source,
      candidateGraphNodeCount,
      candidateSyntheticNodeCount,
      expectedDecision,
    ) => {
      const result = classifyGraphMapRenderBudgetNodeCount({
        candidateGraphNodeCount,
        candidateSyntheticNodeCount,
      })

      expect(result.decision).toBe(expectedDecision)
    },
  )

  it.each([
    [{ warnAtNodes: 0 }, 'warn-at-nodes-invalid'],
    [{ warnAtNodes: 1.5 }, 'warn-at-nodes-invalid'],
    [{ warnAtNodes: Number.POSITIVE_INFINITY }, 'warn-at-nodes-invalid'],
    [{ blockAboveNodes: -1 }, 'block-above-nodes-invalid'],
    [
      { warnAtNodes: 1_201, blockAboveNodes: 1_200 },
      'warn-at-nodes-exceeds-block-above-nodes',
    ],
  ] as const)(
    'falls back both settings for invalid configuration %#',
    (received, issueCode) => {
      const result = normalizeGraphMapRenderBudgetSettings(received)

      expect(result.settings).toBe(
        DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS,
      )
      expect(result.settings).toEqual({
        warnAtNodes: 1_000,
        blockAboveNodes: 1_200,
      })
      expect(result.configurationError).toMatchObject({
        code: 'invalid-graph-map-render-budget',
        fallback: DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS,
      })
      expect(
        result.configurationError?.issues.map(({ code }) => code),
      ).toContain(issueCode)
    },
  )

  it('keeps valid custom settings and preserves inclusive warning semantics', () => {
    const result = classifyGraphMapRenderBudgetNodeCount(
      {
        candidateGraphNodeCount: 20,
        candidateSyntheticNodeCount: 0,
      },
      { warnAtNodes: 20, blockAboveNodes: 30 },
    )

    expect(result).toMatchObject({
      decision: 'warn',
      thresholds: { warnAtNodes: 20, blockAboveNodes: 30 },
      configurationError: null,
    })
  })

  it('freezes the complete source and decision vocabularies', () => {
    const expectedSources: readonly GraphMapRenderBudgetSource[] = [
      'initial',
      'graph-update',
      'node-toggle',
      'show-more',
      'expand-all',
      'search',
      'controlled-view',
    ]

    expect(GRAPH_MAP_RENDER_BUDGET_SOURCES).toEqual(expectedSources)
    expect(GRAPH_MAP_RENDER_BUDGET_DECISIONS).toEqual([
      'allow',
      'warn',
      'block',
    ])
  })

  it('supports replacement-graph and controlled-view event sources', () => {
    const eventFor = (
      source: GraphMapRenderBudgetSource,
    ): GraphMapRenderBudgetEvent => ({
      source,
      decision: 'warn',
      currentGraphNodeCount: 10,
      candidateGraphNodeCount: 999,
      candidateSyntheticNodeCount: 1,
      candidateTotalNodeCount: 1_000,
      currentEdgeCount: 9,
      candidateEdgeCount: 999,
      candidateCountAccuracy: 'exact',
      thresholds: DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS,
      preflightDurationMs: 0.25,
    })

    expect([eventFor('graph-update'), eventFor('controlled-view')]).toEqual([
      expect.objectContaining({ source: 'graph-update', decision: 'warn' }),
      expect.objectContaining({ source: 'controlled-view', decision: 'warn' }),
    ])
  })

  it('allows a lower-bound count only on a blocking event', () => {
    const event: GraphMapRenderBudgetEvent = {
      source: 'expand-all',
      decision: 'block',
      currentGraphNodeCount: 10,
      candidateGraphNodeCount: 1_201,
      candidateSyntheticNodeCount: 0,
      candidateTotalNodeCount: 1_201,
      candidateCountAccuracy: 'at-least',
      thresholds: DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS,
      preflightDurationMs: 0.25,
      blockReason: 'candidate-total-node-count-exceeds-block-threshold',
    }

    expect(event).toMatchObject({
      decision: 'block',
      candidateCountAccuracy: 'at-least',
    })
  })

  it('freezes lifecycle phases through warned and blocked outcomes', () => {
    expect(GRAPH_MAP_VIEW_LIFECYCLE_PHASES).toEqual([
      'preflight-completed',
      'layout-completed',
      'react-nodes-committed',
      'deferred-edges-committed',
      'viewport-fit-completed',
      'view-warned',
      'view-blocked',
    ])
  })
})
