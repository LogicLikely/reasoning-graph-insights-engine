import type {
  GraphEvidenceDetails,
  GraphFixture,
  GraphFixtureEdge,
  GraphFixtureNode,
} from '../fixtures/sampleGraph'

export const BROWSER_JOURNEY_CONTRACT_VERSION = 'phase-4-browser-v1'
export const BROWSER_JOURNEY_PHASE_EVENT = 'logiclikely:insights-benchmark-phase'
export const BROWSER_JOURNEY_COMPLETE_EVENT = 'logiclikely:insights-benchmark-complete'
export const BROWSER_JOURNEY_RESULT_SELECTOR = '[data-testid="insights-browser-benchmark-result"]'

declare const __LOGICLIKELY_INSIGHTS_HARNESS_BUILD__: string | undefined

export const BROWSER_HARNESS_BUILD_IDENTITY =
  typeof __LOGICLIKELY_INSIGHTS_HARNESS_BUILD__ === 'string'
    ? __LOGICLIKELY_INSIGHTS_HARNESS_BUILD__
    : 'unconfigured'

export type BrowserJourneyAction =
  | 'collapsed'
  | 'full-expansion'
  | 'search'
  | 'result-render'

export type TimingBoundaryProvenance =
  | 'directly-instrumented'
  | 'externally-observed'
  | 'estimated'

export interface BrowserJourneyPhase {
  layer: string
  phase: string
  durationMilliseconds: number
  timingBoundaryProvenance: TimingBoundaryProvenance
  source: string
  evidence: Record<string, unknown>
}

export interface BoundedAnalysisResultPayload {
  operationId?: string
  status?: string
  title?: string
  totalResultCardinality: number
  resultDigest?: string | null
  summary?: Record<string, unknown>
  distribution?: unknown
  topItems?: Array<Record<string, unknown>>
  items?: Array<Record<string, unknown>>
  orderedPaths?: Array<Record<string, unknown>>
  failure?: {
    kind: string
    message: string
  } | null
}

export interface BrowserJourneyInput {
  runId?: string
  sampleId?: string
  scenarioId?: string
  correlationId?: string
  action?: BrowserJourneyAction
  graphSlug?: string
  apiBaseUrl?: string
  searchQuery?: string
  resultPayload?: BoundedAnalysisResultPayload
}

export interface BrowserJourneyConfig {
  runId: string
  sampleId: string
  scenarioId: string
  correlationId: string
  action: BrowserJourneyAction
  graphSlug: string
  apiBaseUrl: string | null
  searchQuery: string | null
}

export interface BrowserJourneyFailure {
  code: string
  message: string
  exceptionType?: string | null
}

export interface BrowserJourneyTerminalEvidence {
  completedAtEpochMilliseconds: number
  completedAtMonotonicMilliseconds: number
  stableSelector: string
  stableState: string
  stableFrameCount: number
  viewportTransform: string | null
  searchStatus: string | null
  harnessBuildIdentity: string
  nextHopProtocol: string | null
  resourceTimingLimitation: string | null
  notes: string[]
}

export interface BrowserJourneyResult {
  version: typeof BROWSER_JOURNEY_CONTRACT_VERSION
  scenarioId: string
  runId: string
  sampleId: string
  correlationId: string
  status: 'succeeded' | 'failed' | 'timed-out' | 'cancelled' | 'crashed' | 'skipped'
  phases: BrowserJourneyPhase[]
  actualNodeCount: number | null
  actualEdgeCount: number | null
  renderedNodeCount: number | null
  renderedEdgeCount: number | null
  matchCount: number | null
  requiredAncestorUnionCount: number | null
  /** Complete visible required-node union, including matching nodes. */
  requiredAncestorNodeIds: string[] | null
  matchNodeIds: null
  totalResultCardinality: number | null
  boundedResultItemCount: number | null
  responseBytes: number | null
  requestBytes: null
  responsePayloadSha256: string | null
  observedDatasetFingerprint: null
  identityLimitation: string | null
  driverPayload: null
  failure: BrowserJourneyFailure | null
  unexpectedConsoleErrors: string[]
  pageErrors: string[]
  exactSuppressions: string[]
  evidence: BrowserJourneyTerminalEvidence
}

export interface BrowserJourneyWindowState {
  version: typeof BROWSER_JOURNEY_CONTRACT_VERSION
  state: 'running' | 'completed'
  result: BrowserJourneyResult | null
}

export interface ObservedGraphMapSearchStatus {
  statusText: string
  matchCount: number
  requiredAncestorUnionCount: number
  /** Complete logical search-result cardinality; the required-node union is presentation evidence. */
  totalResultCardinality: number
}

declare global {
  interface Window {
    __logiclikelyInsightsBenchmark?: BrowserJourneyWindowState
    __logiclikelyInsightsBenchmarkInput?: BrowserJourneyInput
  }
}

const ACTIONS = new Set<BrowserJourneyAction>([
  'collapsed',
  'full-expansion',
  'search',
  'result-render',
])

function queryValue(searchParams: URLSearchParams, name: string) {
  const value = searchParams.get(name)?.trim()
  return value ? value : undefined
}

function inputValue(
  searchParams: URLSearchParams,
  queryName: string,
  fallback: string | undefined,
) {
  return queryValue(searchParams, queryName) ?? fallback
}

export function readBrowserJourneyConfig(
  search = window.location.search,
  input = window.__logiclikelyInsightsBenchmarkInput,
): BrowserJourneyConfig {
  const searchParams = new URLSearchParams(search)
  const requestedAction = inputValue(
    searchParams,
    'benchmarkAction',
    input?.action,
  )
  const action = requestedAction && ACTIONS.has(requestedAction as BrowserJourneyAction)
    ? requestedAction as BrowserJourneyAction
    : 'collapsed'

  return {
    runId: inputValue(searchParams, 'benchmarkRunId', input?.runId) ?? 'browser-local-run',
    sampleId: inputValue(searchParams, 'benchmarkSampleId', input?.sampleId) ?? 'browser-local-sample',
    scenarioId: inputValue(searchParams, 'benchmarkScenarioId', input?.scenarioId) ?? 'browser.fixture.collapsed',
    correlationId: inputValue(searchParams, 'benchmarkCorrelationId', input?.correlationId) ?? crypto.randomUUID(),
    action,
    graphSlug: inputValue(searchParams, 'benchmarkGraphSlug', input?.graphSlug) ?? 'sample-medium',
    apiBaseUrl: inputValue(searchParams, 'benchmarkApiBaseUrl', input?.apiBaseUrl) ?? null,
    searchQuery: inputValue(searchParams, 'benchmarkSearchQuery', input?.searchQuery) ?? null,
  }
}

/** Parses only GraphMap's existing visible status; it does not recreate search. */
export function parseGraphMapSearchStatus(
  text: string,
): ObservedGraphMapSearchStatus | null {
  const resultMatch = text.match(/(\d+) matching nodes\s*·\s*(\d+) total shown/i)
  if (resultMatch) {
    return {
      statusText: resultMatch[0],
      matchCount: Number(resultMatch[1]),
      requiredAncestorUnionCount: Number(resultMatch[2]),
      totalResultCardinality: Number(resultMatch[1]),
    }
  }
  if (/No results\./i.test(text)) {
    return {
      statusText: 'No results.',
      matchCount: 0,
      requiredAncestorUnionCount: 0,
      totalResultCardinality: 0,
    }
  }

  return null
}

function recordValue(value: unknown, path: string): Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${path} must be an object.`)
  }

  return value as Record<string, unknown>
}

function stringValue(value: unknown, path: string): string {
  if (typeof value !== 'string') {
    throw new Error(`${path} must be a string.`)
  }

  return value
}

function numberValue(value: unknown, path: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new Error(`${path} must be a finite number.`)
  }

  return value
}

function optionalString(value: unknown, path: string): string | undefined {
  if (value === null || value === undefined) {
    return undefined
  }

  return stringValue(value, path)
}

function stringArray(value: unknown, path: string): string[] {
  if (!Array.isArray(value)) {
    throw new Error(`${path} must be an array.`)
  }

  return value.map((item, index) => stringValue(item, `${path}[${index}]`))
}

function mapEvidence(value: unknown, path: string): GraphEvidenceDetails | undefined {
  if (value === null || value === undefined) {
    return undefined
  }

  const evidence = recordValue(value, path)
  return {
    type: stringValue(evidence.type, `${path}.type`) as GraphEvidenceDetails['type'],
    score: numberValue(evidence.score, `${path}.score`),
    rationale: optionalString(evidence.rationale, `${path}.rationale`),
  }
}

function mapNode(value: unknown, index: number): GraphFixtureNode {
  const path = `graph.nodes[${index}]`
  const node = recordValue(value, path)
  return {
    id: stringValue(node.id, `${path}.id`),
    kind: stringValue(node.kind, `${path}.kind`) as GraphFixtureNode['kind'],
    title: stringValue(node.title, `${path}.title`),
    bodyText: stringValue(node.bodyText, `${path}.bodyText`),
    category: optionalString(node.category, `${path}.category`),
    tags: stringArray(node.tags ?? [], `${path}.tags`),
    priorOdds: numberValue(node.priorOdds, `${path}.priorOdds`),
    posteriorOdds: numberValue(node.posteriorOdds, `${path}.posteriorOdds`),
    evidence: mapEvidence(node.evidence, `${path}.evidence`),
  }
}

function mapEdge(value: unknown, index: number): GraphFixtureEdge {
  const path = `graph.edges[${index}]`
  const edge = recordValue(value, path)
  return {
    id: stringValue(edge.id, `${path}.id`),
    from: stringValue(edge.from, `${path}.from`),
    to: stringValue(edge.to, `${path}.to`),
    kind: stringValue(edge.kind, `${path}.kind`) as GraphFixtureEdge['kind'],
    importanceToParent: numberValue(
      edge.importanceToParent,
      `${path}.importanceToParent`,
    ),
  }
}

/** Explicit API DTO to consumer-domain mapping measured by the browser harness. */
export function mapApiGraphToDomain(value: unknown): GraphFixture {
  const graph = recordValue(value, 'graph')
  if (!Array.isArray(graph.nodes) || !Array.isArray(graph.edges)) {
    throw new Error('graph.nodes and graph.edges must be arrays.')
  }

  return {
    slug: stringValue(graph.slug, 'graph.slug'),
    title: stringValue(graph.title, 'graph.title'),
    description: optionalString(graph.description, 'graph.description') ?? '',
    nodes: graph.nodes.map(mapNode),
    edges: graph.edges.map(mapEdge),
  }
}

export async function sha256Bytes(bytes: ArrayBuffer): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', bytes)
  const hex = Array.from(new Uint8Array(digest), (byte) => (
    byte.toString(16).padStart(2, '0')
  )).join('')
  return `sha256:${hex}`
}

export function errorFailure(error: unknown, kind = 'browser-harness-error'): BrowserJourneyFailure {
  if (error instanceof Error) {
    return {
      code: kind,
      message: error.message,
      exceptionType: error.name,
    }
  }

  return {
    code: kind,
    message: String(error),
    exceptionType: null,
  }
}
