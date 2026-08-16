export type GraphMapRenderBudgetSettings = Readonly<{
  warnAtNodes?: number
  blockAboveNodes?: number
}>

export type ResolvedGraphMapRenderBudgetSettings = Readonly<{
  warnAtNodes: number
  blockAboveNodes: number
}>

export const DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS: ResolvedGraphMapRenderBudgetSettings =
  Object.freeze({
    warnAtNodes: 1_000,
    blockAboveNodes: 1_200,
  })

export const GRAPH_MAP_RENDER_BUDGET_SOURCES = [
  'initial',
  'graph-update',
  'node-toggle',
  'show-more',
  'expand-all',
  'search',
  'controlled-view',
] as const

export type GraphMapRenderBudgetSource =
  (typeof GRAPH_MAP_RENDER_BUDGET_SOURCES)[number]

export const GRAPH_MAP_RENDER_BUDGET_DECISIONS = [
  'allow',
  'warn',
  'block',
] as const

export type GraphMapRenderBudgetDecision =
  (typeof GRAPH_MAP_RENDER_BUDGET_DECISIONS)[number]

export const GRAPH_MAP_VIEW_LIFECYCLE_PHASES = [
  'preflight-completed',
  'layout-completed',
  'react-nodes-committed',
  'deferred-edges-committed',
  'viewport-fit-completed',
  'view-warned',
  'view-blocked',
] as const

export type GraphMapViewLifecyclePhase =
  (typeof GRAPH_MAP_VIEW_LIFECYCLE_PHASES)[number]

export type GraphMapRenderBudgetConfigurationIssue = Readonly<{
  code:
    | 'warn-at-nodes-invalid'
    | 'block-above-nodes-invalid'
    | 'warn-at-nodes-exceeds-block-above-nodes'
  fields: readonly ('warnAtNodes' | 'blockAboveNodes')[]
  receivedValues: readonly (number | undefined)[]
}>

export type GraphMapRenderBudgetConfigurationError = Readonly<{
  code: 'invalid-graph-map-render-budget'
  message: string
  issues: readonly GraphMapRenderBudgetConfigurationIssue[]
  received: GraphMapRenderBudgetSettings
  fallback: ResolvedGraphMapRenderBudgetSettings
}>

export type GraphMapRenderBudgetNormalization = Readonly<{
  settings: ResolvedGraphMapRenderBudgetSettings
  configurationError: GraphMapRenderBudgetConfigurationError | null
}>

export type GraphMapRenderBudgetNodeCountInput = Readonly<{
  candidateGraphNodeCount: number
  candidateSyntheticNodeCount: number
}>

export type GraphMapRenderBudgetNodeCountClassification = Readonly<{
  decision: GraphMapRenderBudgetDecision
  candidateGraphNodeCount: number
  candidateSyntheticNodeCount: number
  candidateTotalNodeCount: number
  thresholds: ResolvedGraphMapRenderBudgetSettings
  configurationError: GraphMapRenderBudgetConfigurationError | null
}>

export type GraphMapRenderBudgetCandidateCountAccuracy = 'exact' | 'at-least'

export type GraphMapRenderBudgetBlockReason =
  'candidate-total-node-count-exceeds-block-threshold'

type GraphMapRenderBudgetEventBase = Readonly<{
  source: GraphMapRenderBudgetSource
  currentGraphNodeCount: number
  candidateGraphNodeCount: number
  candidateSyntheticNodeCount: number
  candidateTotalNodeCount: number
  currentEdgeCount?: number
  candidateEdgeCount?: number
  thresholds: ResolvedGraphMapRenderBudgetSettings
  preflightDurationMs: number
  configurationError?: GraphMapRenderBudgetConfigurationError
}>

export type GraphMapRenderBudgetEvent =
  | (GraphMapRenderBudgetEventBase &
      Readonly<{
        decision: 'allow' | 'warn'
        candidateCountAccuracy: 'exact'
        blockReason?: never
      }>)
  | (GraphMapRenderBudgetEventBase &
      Readonly<{
        decision: 'block'
        candidateCountAccuracy: GraphMapRenderBudgetCandidateCountAccuracy
        blockReason: GraphMapRenderBudgetBlockReason
      }>)

const isFinitePositiveInteger = (value: number): boolean =>
  Number.isFinite(value) && Number.isInteger(value) && value > 0

const requireNonNegativeInteger = (value: number, field: string): void => {
  if (!Number.isFinite(value) || !Number.isInteger(value) || value < 0) {
    throw new RangeError(`${field} must be a finite non-negative integer`)
  }
}

/**
 * Resolves omitted settings to their defaults. If any supplied setting is
 * invalid, both settings fall back together so malformed configuration can
 * never weaken the shared safeguard.
 */
export const normalizeGraphMapRenderBudgetSettings = (
  received: GraphMapRenderBudgetSettings = {},
): GraphMapRenderBudgetNormalization => {
  const warnAtNodes =
    received.warnAtNodes ?? DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS.warnAtNodes
  const blockAboveNodes =
    received.blockAboveNodes ??
    DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS.blockAboveNodes
  const issues: GraphMapRenderBudgetConfigurationIssue[] = []

  if (!isFinitePositiveInteger(warnAtNodes)) {
    issues.push({
      code: 'warn-at-nodes-invalid',
      fields: ['warnAtNodes'],
      receivedValues: [received.warnAtNodes],
    })
  }

  if (!isFinitePositiveInteger(blockAboveNodes)) {
    issues.push({
      code: 'block-above-nodes-invalid',
      fields: ['blockAboveNodes'],
      receivedValues: [received.blockAboveNodes],
    })
  }

  if (
    isFinitePositiveInteger(warnAtNodes) &&
    isFinitePositiveInteger(blockAboveNodes) &&
    warnAtNodes > blockAboveNodes
  ) {
    issues.push({
      code: 'warn-at-nodes-exceeds-block-above-nodes',
      fields: ['warnAtNodes', 'blockAboveNodes'],
      receivedValues: [warnAtNodes, blockAboveNodes],
    })
  }

  if (issues.length > 0) {
    const configurationError: GraphMapRenderBudgetConfigurationError = {
      code: 'invalid-graph-map-render-budget',
      message:
        'GraphMap render-budget settings are invalid; both safe defaults were applied.',
      issues,
      received,
      fallback: DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS,
    }

    return {
      settings: DEFAULT_GRAPH_MAP_RENDER_BUDGET_SETTINGS,
      configurationError,
    }
  }

  return {
    settings: Object.freeze({ warnAtNodes, blockAboveNodes }),
    configurationError: null,
  }
}

/**
 * Classifies already-counted candidate nodes. Projection traversal, cycle
 * handling, edge counting, layout, and rendering deliberately remain outside
 * this Phase 0 contract helper.
 */
export const classifyGraphMapRenderBudgetNodeCount = (
  input: GraphMapRenderBudgetNodeCountInput,
  receivedSettings: GraphMapRenderBudgetSettings = {},
): GraphMapRenderBudgetNodeCountClassification => {
  requireNonNegativeInteger(
    input.candidateGraphNodeCount,
    'candidateGraphNodeCount',
  )
  requireNonNegativeInteger(
    input.candidateSyntheticNodeCount,
    'candidateSyntheticNodeCount',
  )

  const candidateTotalNodeCount =
    input.candidateGraphNodeCount + input.candidateSyntheticNodeCount
  requireNonNegativeInteger(candidateTotalNodeCount, 'candidateTotalNodeCount')

  const normalized = normalizeGraphMapRenderBudgetSettings(receivedSettings)
  const decision: GraphMapRenderBudgetDecision =
    candidateTotalNodeCount > normalized.settings.blockAboveNodes
      ? 'block'
      : candidateTotalNodeCount >= normalized.settings.warnAtNodes
        ? 'warn'
        : 'allow'

  return {
    decision,
    candidateGraphNodeCount: input.candidateGraphNodeCount,
    candidateSyntheticNodeCount: input.candidateSyntheticNodeCount,
    candidateTotalNodeCount,
    thresholds: normalized.settings,
    configurationError: normalized.configurationError,
  }
}
