import {
  Profiler,
  useCallback,
  useEffect,
  useRef,
  useState,
  type ProfilerOnRenderCallback,
} from 'react'
import { InsightsGraphCanvas } from '../components/graph/InsightsGraphCanvas'
import { sampleGraph } from '../fixtures/sampleGraph'
import type { GraphFixture } from '../fixtures/sampleGraph'
import { BoundedAnalysisResult, MAX_RENDERED_RESULT_ITEMS } from './BoundedAnalysisResult'
import {
  BROWSER_JOURNEY_COMPLETE_EVENT,
  BROWSER_JOURNEY_CONTRACT_VERSION,
  BROWSER_HARNESS_BUILD_IDENTITY,
  BROWSER_JOURNEY_PHASE_EVENT,
  BROWSER_JOURNEY_RESULT_SELECTOR,
  errorFailure,
  mapApiGraphToDomain,
  parseGraphMapSearchStatus,
  readBrowserJourneyConfig,
  sha256Bytes,
  type BoundedAnalysisResultPayload,
  type BrowserJourneyConfig,
  type BrowserJourneyPhase,
  type BrowserJourneyResult,
  type BrowserJourneyTerminalEvidence,
  type TimingBoundaryProvenance,
} from './browserJourneyContract'
import { observeGraphResourceTiming } from './graphResourceTiming'
import './InsightsBrowserPerformanceHarness.css'

const STABLE_FRAME_TARGET = 4
const GRAPH_STABLE_TIMEOUT_MILLISECONDS = 30_000
const GRAPHMAP_SEARCH_PLACEHOLDER = 'Type 3+ characters…'

interface GraphDomSnapshot {
  renderedNodeCount: number
  renderedEdgeCount: number
  nodeIds: string[]
  viewportTransform: string | null
  signature: string
}

interface StableGraphObservation {
  startMilliseconds: number
  firstNodeMilliseconds: number
  firstEdgeMilliseconds: number | null
  endMilliseconds: number
  stableFrameCount: number
  snapshot: GraphDomSnapshot
}

interface GraphResponseEvidence {
  responseBytes: number
  responsePayloadSha256: string
  responseStatus: number
  responseHeaders: Record<string, string>
  nextHopProtocol: string | null
  resourceTimingLimitation: string | null
}

interface ReactCommitMeasurement {
  profilerId: string
  reactPhase: 'mount' | 'update' | 'nested-update'
  actualDurationMilliseconds: number
  baseDurationMilliseconds: number
  startTimeMilliseconds: number
  commitTimeMilliseconds: number
}

type TerminalEvidenceInput = Omit<
  BrowserJourneyTerminalEvidence,
  'harnessBuildIdentity' | 'nextHopProtocol' | 'resourceTimingLimitation'
> & Partial<Pick<
  BrowserJourneyTerminalEvidence,
  'nextHopProtocol' | 'resourceTimingLimitation'
>>

function nextAnimationFrame(): Promise<number> {
  return new Promise((resolve) => requestAnimationFrame(resolve))
}

function graphDomSnapshot(root: HTMLElement): GraphDomSnapshot {
  const nodes = [...root.querySelectorAll<HTMLElement>('.react-flow__node[data-id]')]
  const edges = [...root.querySelectorAll<HTMLElement>('.react-flow__edge')]
  const edgePaths = [...root.querySelectorAll<SVGPathElement>('.react-flow__edge-path')]
  const nodeIds = nodes
    .map((node) => node.dataset.id)
    .filter((id): id is string => Boolean(id))
  const nodePositions = nodes.map((node) => `${node.dataset.id}:${node.style.transform}`)
  const viewport = root.querySelector<HTMLElement>('.react-flow__viewport')
  const viewportTransform = viewport?.style.transform || null

  return {
    renderedNodeCount: nodes.length,
    renderedEdgeCount: edges.length,
    nodeIds,
    viewportTransform,
    signature: JSON.stringify([
      nodeIds,
      nodePositions,
      edges.length,
      edgePaths.map((path) => path.getAttribute('d')),
      viewportTransform,
    ]),
  }
}

async function observeStableGraph(
  root: HTMLElement,
  options: {
    startMilliseconds: number
    minimumNodeCount: number
    requireAnEdge: boolean
  },
): Promise<StableGraphObservation> {
  const deadline = options.startMilliseconds + GRAPH_STABLE_TIMEOUT_MILLISECONDS
  let firstNodeMilliseconds: number | null = null
  let firstEdgeMilliseconds: number | null = null
  let stableFrameCount = 0
  let priorSignature: string | null = null
  let snapshot = graphDomSnapshot(root)

  while (performance.now() <= deadline) {
    await nextAnimationFrame()
    const observedAt = performance.now()
    snapshot = graphDomSnapshot(root)

    if (firstNodeMilliseconds === null && snapshot.renderedNodeCount > 0) {
      firstNodeMilliseconds = observedAt
    }
    if (firstEdgeMilliseconds === null && snapshot.renderedEdgeCount > 0) {
      firstEdgeMilliseconds = observedAt
    }

    const hasExpectedNodes = snapshot.renderedNodeCount >= options.minimumNodeCount
    const hasExpectedEdges = !options.requireAnEdge || snapshot.renderedEdgeCount > 0
    if (hasExpectedNodes && hasExpectedEdges && snapshot.signature === priorSignature) {
      stableFrameCount += 1
    } else {
      stableFrameCount = 0
    }
    priorSignature = snapshot.signature

    if (stableFrameCount >= STABLE_FRAME_TARGET) {
      return {
        startMilliseconds: options.startMilliseconds,
        firstNodeMilliseconds: firstNodeMilliseconds ?? observedAt,
        firstEdgeMilliseconds,
        endMilliseconds: observedAt,
        stableFrameCount,
        snapshot,
      }
    }
  }

  throw new Error(
    `GraphMap did not reach a stable DOM state within ${GRAPH_STABLE_TIMEOUT_MILLISECONDS}ms. ` +
    `Observed ${snapshot.renderedNodeCount} nodes and ${snapshot.renderedEdgeCount} edges.`,
  )
}

function setReactInputValue(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(
    HTMLInputElement.prototype,
    'value',
  )?.set
  setter?.call(input, value)
  input.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText' }))
}

async function waitForSearchStatus(root: HTMLElement): Promise<{
  endMilliseconds: number
  statusText: string
  matchCount: number
  requiredAncestorUnionCount: number
  totalResultCardinality: number
}> {
  const deadline = performance.now() + GRAPH_STABLE_TIMEOUT_MILLISECONDS
  while (performance.now() <= deadline) {
    await nextAnimationFrame()
    const dialog = root.querySelector<HTMLElement>('[role="dialog"][aria-label="Search graph"]')
    const text = dialog?.textContent ?? ''
    const status = parseGraphMapSearchStatus(text)
    if (status) {
      return {
        endMilliseconds: performance.now(),
        ...status,
      }
    }
  }

  throw new Error('GraphMap did not expose a terminal search status before the browser timeout.')
}

function responseHeaderEvidence(response: Response): Record<string, string> {
  return Object.fromEntries([...response.headers.entries()].sort(([left], [right]) => (
    left.localeCompare(right)
  )))
}

function phaseEvidence(
  startMilliseconds: number,
  endMilliseconds: number,
  evidence: Record<string, unknown>,
) {
  return {
    startMilliseconds,
    endMilliseconds,
    ...evidence,
  }
}

function safeMark(name: string, startTime: number) {
  try {
    performance.mark(name, { startTime })
  } catch {
    // Older test DOMs may not implement custom mark start times. The numeric
    // raw evidence remains authoritative and is always emitted.
  }
}

function resultPayloadItemCount(payload: BoundedAnalysisResultPayload) {
  return Math.min(
    payload.topItems?.length ?? payload.items?.length ?? 0,
    MAX_RENDERED_RESULT_ITEMS,
  )
}

export interface InsightsBrowserPerformanceHarnessProps {
  fixtureGraph?: GraphFixture
  resultPayload?: BoundedAnalysisResultPayload
  config?: BrowserJourneyConfig
}

export function InsightsBrowserPerformanceHarness({
  fixtureGraph = sampleGraph,
  resultPayload: suppliedResultPayload,
  config: suppliedConfig,
}: InsightsBrowserPerformanceHarnessProps) {
  const [config] = useState(() => suppliedConfig ?? readBrowserJourneyConfig())
  const resultPayload = suppliedResultPayload ?? window.__logiclikelyInsightsBenchmarkInput?.resultPayload
  const [graph, setGraph] = useState<GraphFixture | null>(null)
  const [terminalResult, setTerminalResult] = useState<BrowserJourneyResult | null>(null)
  const [isFullscreen, setIsFullscreen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const phasesRef = useRef<BrowserJourneyPhase[]>([])
  const phaseSequenceRef = useRef(0)
  const completedRef = useRef(false)
  const graphRenderStartRef = useRef<number | null>(null)
  const [resultRenderStartMilliseconds] = useState(() => performance.now())
  const responseRef = useRef<GraphResponseEvidence | null>(null)
  const actualCountsRef = useRef<{ nodes: number; edges: number } | null>(null)
  const pageErrorsRef = useRef<string[]>([])
  const unexpectedConsoleErrorsRef = useRef<string[]>([])
  const graphReactCommitsRef = useRef<ReactCommitMeasurement[]>([])
  const resultReactCommitRecordedRef = useRef(false)
  const graphMapAdapterRecordedRef = useRef(false)

  useEffect(() => {
    window.__logiclikelyInsightsBenchmark = {
      version: BROWSER_JOURNEY_CONTRACT_VERSION,
      state: 'running',
      result: null,
    }
  }, [])

  const recordPhase = useCallback((phase: BrowserJourneyPhase) => {
    if (completedRef.current) {
      return
    }

    const sequence = phaseSequenceRef.current++
    const startMilliseconds = Number(phase.evidence.startMilliseconds)
    const endMilliseconds = Number(phase.evidence.endMilliseconds)
    const startMark = `logiclikely:${config.sampleId}:${sequence}:${phase.layer}:${phase.phase}:start`
    const endMark = `logiclikely:${config.sampleId}:${sequence}:${phase.layer}:${phase.phase}:end`
    safeMark(startMark, startMilliseconds)
    safeMark(endMark, endMilliseconds)
    const measuredPhase = {
      ...phase,
      durationMilliseconds: Math.max(0, phase.durationMilliseconds),
      evidence: {
        ...phase.evidence,
        startMark,
        endMark,
        sequence,
      },
    }
    phasesRef.current.push(measuredPhase)
    window.dispatchEvent(new CustomEvent(BROWSER_JOURNEY_PHASE_EVENT, {
      detail: measuredPhase,
    }))
  }, [config.sampleId])

  const recordBoundary = useCallback((
    layer: string,
    phase: string,
    startMilliseconds: number,
    endMilliseconds: number,
    timingBoundaryProvenance: TimingBoundaryProvenance,
    source: string,
    evidence: Record<string, unknown>,
  ) => {
    recordPhase({
      layer,
      phase,
      durationMilliseconds: Math.max(0, endMilliseconds - startMilliseconds),
      timingBoundaryProvenance,
      source,
      evidence: phaseEvidence(startMilliseconds, endMilliseconds, evidence),
    })
  }, [recordPhase])

  const complete = useCallback((
    status: BrowserJourneyResult['status'],
    terminalEvidence: TerminalEvidenceInput,
    details: Partial<BrowserJourneyResult> = {},
  ) => {
    if (completedRef.current) {
      return
    }

    completedRef.current = true
    const actual = actualCountsRef.current
    const response = responseRef.current
    const hasErrors = unexpectedConsoleErrorsRef.current.length > 0 || pageErrorsRef.current.length > 0
    const effectiveStatus = status === 'succeeded' && hasErrors ? 'failed' : status
    const result: BrowserJourneyResult = {
      version: BROWSER_JOURNEY_CONTRACT_VERSION,
      scenarioId: config.scenarioId,
      runId: config.runId,
      sampleId: config.sampleId,
      correlationId: config.correlationId,
      phases: [...phasesRef.current],
      actualNodeCount: actual?.nodes ?? null,
      actualEdgeCount: actual?.edges ?? null,
      renderedNodeCount: null,
      renderedEdgeCount: null,
      matchCount: null,
      requiredAncestorUnionCount: null,
      requiredAncestorNodeIds: null,
      matchNodeIds: null,
      totalResultCardinality: null,
      boundedResultItemCount: null,
      responseBytes: response?.responseBytes ?? null,
      requestBytes: null,
      responsePayloadSha256: response?.responsePayloadSha256 ?? null,
      observedDatasetFingerprint: null,
      identityLimitation: response
        ? 'responsePayloadSha256 hashes exact response bytes and is not the canonical dataset fingerprint.'
        : 'The fixture-only Storybook preview has no REST payload or canonical dataset fingerprint.',
      driverPayload: null,
      failure: hasErrors && !details.failure
        ? {
            code: 'browser-page-error',
            message: 'The harness observed an unexpected page or console error.',
          }
        : null,
      unexpectedConsoleErrors: [...unexpectedConsoleErrorsRef.current],
      pageErrors: [...pageErrorsRef.current],
      exactSuppressions: [],
      evidence: {
        ...terminalEvidence,
        harnessBuildIdentity: BROWSER_HARNESS_BUILD_IDENTITY,
        nextHopProtocol:
          terminalEvidence.nextHopProtocol ?? response?.nextHopProtocol ?? null,
        resourceTimingLimitation:
          terminalEvidence.resourceTimingLimitation ??
          response?.resourceTimingLimitation ??
          'This journey made no graph REST request, so no graph resource timing was observed.',
      },
      ...details,
      status: effectiveStatus,
    }
    window.__logiclikelyInsightsBenchmark = {
      version: BROWSER_JOURNEY_CONTRACT_VERSION,
      state: 'completed',
      result,
    }
    setTerminalResult(result)
    window.dispatchEvent(new CustomEvent(BROWSER_JOURNEY_COMPLETE_EVENT, { detail: result }))
  }, [config])

  const fail = useCallback((error: unknown) => {
    const now = performance.now()
    complete('failed', {
      completedAtEpochMilliseconds: Date.now(),
      completedAtMonotonicMilliseconds: now,
      stableSelector: BROWSER_JOURNEY_RESULT_SELECTOR,
      stableState: 'failed',
      stableFrameCount: 0,
      viewportTransform: null,
      searchStatus: null,
      notes: ['The harness retained every phase that completed before failure.'],
    }, {
      failure: errorFailure(error),
    })
  }, [complete])

  useEffect(() => {
    const onError = (event: ErrorEvent) => {
      pageErrorsRef.current.push(event.message || 'Unknown window error')
    }
    const onUnhandledRejection = (event: PromiseRejectionEvent) => {
      pageErrorsRef.current.push(
        event.reason instanceof Error ? event.reason.message : String(event.reason),
      )
    }
    window.addEventListener('error', onError)
    window.addEventListener('unhandledrejection', onUnhandledRejection)

    return () => {
      window.removeEventListener('error', onError)
      window.removeEventListener('unhandledrejection', onUnhandledRejection)
    }
  }, [])

  useEffect(() => {
    if (config.action === 'result-render') {
      if (!resultPayload) {
        fail(new Error('The result-render journey requires a bounded resultPayload input.'))
      }
      return
    }

    let active = true
    async function loadGraph() {
      if (!config.apiBaseUrl) {
        actualCountsRef.current = {
          nodes: fixtureGraph.nodes.length,
          edges: fixtureGraph.edges.length,
        }
        graphRenderStartRef.current = performance.now()
        setGraph(fixtureGraph)
        return
      }

      const url = new URL(
        `/api/graphs/${encodeURIComponent(config.graphSlug)}`,
        config.apiBaseUrl,
      )
      const requestStartMilliseconds = performance.now()
      const response = await fetch(url, {
        cache: 'no-store',
        headers: {
          'X-Insights-Run-Id': config.runId,
          'X-Insights-Sample-Id': config.sampleId,
        },
      })
      const headersReceivedMilliseconds = performance.now()
      recordBoundary(
        'transport',
        'time-to-first-byte',
        requestStartMilliseconds,
        headersReceivedMilliseconds,
        'directly-instrumented',
        'browser-performance-mark',
        {
          url: url.href,
          method: 'GET',
          responseStatus: response.status,
          responseHeaders: responseHeaderEvidence(response),
          boundary: 'request-start-to-response-headers',
        },
      )

      if (!response.ok) {
        throw new Error(`Graph fetch failed with HTTP ${response.status}.`)
      }
      const echoedRunId = response.headers.get('X-Insights-Run-Id')
      const echoedSampleId = response.headers.get('X-Insights-Sample-Id')
      if (echoedRunId?.toLowerCase() !== config.runId.toLowerCase() ||
          echoedSampleId?.toLowerCase() !== config.sampleId.toLowerCase()) {
        throw new Error('Graph fetch response did not echo the requested run/sample correlation.')
      }

      const responseBytes = await response.arrayBuffer()
      const transferCompletedMilliseconds = performance.now()
      const resourceTiming = observeGraphResourceTiming(url.href)
      recordBoundary(
        'transport',
        'full-transfer',
        requestStartMilliseconds,
        transferCompletedMilliseconds,
        'directly-instrumented',
        'browser-performance-mark',
        {
          url: url.href,
          responseBytes: responseBytes.byteLength,
          nextHopProtocol: resourceTiming.nextHopProtocol,
          resourceTimingLimitation: resourceTiming.resourceTimingLimitation,
          resourceTiming: resourceTiming.resourceTiming,
          boundary: 'request-start-to-response-body-complete',
        },
      )

      const parseStartMilliseconds = performance.now()
      const parsed = JSON.parse(new TextDecoder().decode(responseBytes)) as unknown
      const parseEndMilliseconds = performance.now()
      recordBoundary(
        'browser-data',
        'json-parse',
        parseStartMilliseconds,
        parseEndMilliseconds,
        'directly-instrumented',
        'browser-performance-mark',
        {
          inputBytes: responseBytes.byteLength,
          parser: 'JSON.parse',
        },
      )

      const mappingStartMilliseconds = performance.now()
      const mappedGraph = mapApiGraphToDomain(parsed)
      const mappingEndMilliseconds = performance.now()
      recordBoundary(
        'browser-data',
        'domain-mapping',
        mappingStartMilliseconds,
        mappingEndMilliseconds,
        'directly-instrumented',
        'browser-performance-mark',
        {
          inputNodeCount: mappedGraph.nodes.length,
          inputEdgeCount: mappedGraph.edges.length,
          mapper: 'mapApiGraphToDomain',
        },
      )

      const responsePayloadSha256 = await sha256Bytes(responseBytes)
      responseRef.current = {
        responseBytes: responseBytes.byteLength,
        responsePayloadSha256,
        responseStatus: response.status,
        responseHeaders: responseHeaderEvidence(response),
        nextHopProtocol: resourceTiming.nextHopProtocol,
        resourceTimingLimitation: resourceTiming.resourceTimingLimitation,
      }
      actualCountsRef.current = {
        nodes: mappedGraph.nodes.length,
        edges: mappedGraph.edges.length,
      }
      graphRenderStartRef.current = performance.now()
      if (active) {
        setGraph(mappedGraph)
      }
    }

    void loadGraph().catch(fail)
    return () => {
      active = false
    }
  }, [config, fail, fixtureGraph, recordBoundary, resultPayload])

  const onGraphMapAdapterMeasured = useCallback((measurement: {
    durationMilliseconds: number
    startTimeMilliseconds: number
    endTimeMilliseconds: number
    nodeCount: number
    edgeCount: number
  }) => {
    if (graphMapAdapterRecordedRef.current) {
      return
    }
    graphMapAdapterRecordedRef.current = true
    recordBoundary(
      'browser-data',
      'graph-map-adapter',
      measurement.startTimeMilliseconds,
      measurement.endTimeMilliseconds,
      'directly-instrumented',
      'consumer-adapter-wrapper',
      {
        nodeCount: measurement.nodeCount,
        edgeCount: measurement.edgeCount,
        invocation: 1,
      },
    )
  }, [recordBoundary])

  const onGraphRender = useCallback<ProfilerOnRenderCallback>((
    id,
    phase,
    actualDuration,
    baseDuration,
    startTime,
    commitTime,
  ) => {
    graphReactCommitsRef.current.push({
      profilerId: id,
      reactPhase: phase,
      actualDurationMilliseconds: actualDuration,
      baseDurationMilliseconds: baseDuration,
      startTimeMilliseconds: startTime,
      commitTimeMilliseconds: commitTime,
    })
  }, [])

  const recordSelectedGraphReactCommit = useCallback((
    action: BrowserJourneyConfig['action'],
    actionStartMilliseconds: number,
    stableEndMilliseconds: number,
  ) => {
    const eligible = graphReactCommitsRef.current.filter((measurement) => (
      measurement.commitTimeMilliseconds >= actionStartMilliseconds &&
      measurement.commitTimeMilliseconds <= stableEndMilliseconds
    ))
    const selected = eligible.at(-1)
    if (!selected) {
      throw new Error(
        `React Profiler exposed no ${action} commit between the action start and stable view.`,
      )
    }

    recordBoundary(
      'graph-map',
      'react-commit',
      selected.startTimeMilliseconds,
      selected.commitTimeMilliseconds,
      'directly-instrumented',
      'react-profiler',
      {
        profilerId: selected.profilerId,
        reactPhase: selected.reactPhase,
        actualDurationMilliseconds: selected.actualDurationMilliseconds,
        baseDurationMilliseconds: selected.baseDurationMilliseconds,
        commitTimeMilliseconds: selected.commitTimeMilliseconds,
        journeyAction: action,
        actionStartMilliseconds,
        stableEndMilliseconds,
        selection: 'last-profiler-commit-from-action-start-through-stable-view',
        observedCommitCount: graphReactCommitsRef.current.length,
        eligibleCommitCount: eligible.length,
        harnessBuildIdentity: BROWSER_HARNESS_BUILD_IDENTITY,
      },
    )
  }, [recordBoundary])

  const onResultRender = useCallback<ProfilerOnRenderCallback>((
    id,
    phase,
    actualDuration,
    baseDuration,
    startTime,
    commitTime,
  ) => {
    if (resultReactCommitRecordedRef.current) {
      return
    }
    resultReactCommitRecordedRef.current = true
    recordBoundary(
      'lab-result',
      'react-commit',
      startTime,
      commitTime,
      'directly-instrumented',
      'react-profiler',
      {
        profilerId: id,
        reactPhase: phase,
        actualDurationMilliseconds: actualDuration,
        baseDurationMilliseconds: baseDuration,
        commitTimeMilliseconds: commitTime,
        harnessBuildIdentity: BROWSER_HARNESS_BUILD_IDENTITY,
      },
    )
    if (!phasesRef.current.some((sample) => (
      sample.layer === 'lab-result' && sample.phase === 'result-render'
    ))) {
      recordBoundary(
        'lab-result',
        'result-render',
        resultRenderStartMilliseconds,
        commitTime,
        'directly-instrumented',
        'consumer-performance-mark-and-react-profiler',
        {
          profilerId: id,
          reactPhase: phase,
          terminalCommitTimeMilliseconds: commitTime,
          harnessBuildIdentity: BROWSER_HARNESS_BUILD_IDENTITY,
        },
      )
    }
  }, [recordBoundary, resultRenderStartMilliseconds])

  useEffect(() => {
    if (!graph || config.action === 'result-render' || !rootRef.current) {
      return
    }

    const activeGraph = graph
    let active = true
    async function runGraphJourney() {
      const root = rootRef.current!
      const graphStart = graphRenderStartRef.current ?? performance.now()
      const initial = await observeStableGraph(root, {
        startMilliseconds: graphStart,
        minimumNodeCount: 1,
        requireAnEdge: activeGraph.edges.length > 0,
      })
      if (!active) return

      recordBoundary(
        'graph-map',
        'node-edge-materialization',
        initial.startMilliseconds,
        initial.firstNodeMilliseconds,
        'externally-observed',
        'consumer-dom-observation',
        {
          renderedNodeCount: initial.snapshot.renderedNodeCount,
          renderedEdgeCount: initial.snapshot.renderedEdgeCount,
          limitation: 'Observed first GraphMap DOM materialization; internal model construction is not exposed by GraphMap 0.2.0.',
        },
      )
      if (initial.firstEdgeMilliseconds !== null) {
        recordBoundary(
          'graph-map',
          'deferred-edge-commit',
          initial.firstNodeMilliseconds,
          initial.firstEdgeMilliseconds,
          'externally-observed',
          'consumer-dom-observation',
          {
            renderedEdgeCount: initial.snapshot.renderedEdgeCount,
            boundary: 'first-node-dom-to-first-edge-dom',
          },
        )
      }
      if (config.action === 'collapsed') {
        recordBoundary(
          'graph-map',
          'dagre-layout',
          initial.startMilliseconds,
          initial.endMilliseconds,
          'estimated',
          'consumer-stable-dom-estimate',
          {
            stableFrameCount: initial.stableFrameCount,
            viewportTransform: initial.snapshot.viewportTransform,
            limitation: 'Aggregate render-to-stable estimate; GraphMap 0.2.0 exposes no internal Dagre lifecycle boundary.',
          },
        )
        recordBoundary(
          'graph-map',
          'viewport-fit',
          initial.firstNodeMilliseconds,
          initial.endMilliseconds,
          'estimated',
          'consumer-animation-frame-settle',
          {
            stableFrameCount: initial.stableFrameCount,
            viewportTransform: initial.snapshot.viewportTransform,
            renderedNodeCount: initial.snapshot.renderedNodeCount,
            renderedEdgeCount: initial.snapshot.renderedEdgeCount,
          },
        )
      }

      let terminal = initial
      let actionStartMilliseconds = graphStart
      let searchStatusText: string | null = null
      let matchCount: number | null = null
      let requiredAncestorUnionCount: number | null = null
      let requiredAncestorNodeIds: string[] | null = null
      let totalResultCardinality: number | null = null

      if (config.action === 'full-expansion') {
        if (activeGraph.nodes.length > 1_000) {
          complete('skipped', {
            completedAtEpochMilliseconds: Date.now(),
            completedAtMonotonicMilliseconds: performance.now(),
            stableSelector: BROWSER_JOURNEY_RESULT_SELECTOR,
            stableState: 'skipped-before-full-expansion',
            stableFrameCount: initial.stableFrameCount,
            viewportTransform: initial.snapshot.viewportTransform,
            searchStatus: null,
            notes: ['Complete GraphMap expansion is limited to the designated 1K dataset.'],
          }, {
            renderedNodeCount: initial.snapshot.renderedNodeCount,
            renderedEdgeCount: initial.snapshot.renderedEdgeCount,
            failure: {
              code: 'browser-full-expansion-small-only',
              message: 'Complete GraphMap expansion is not scheduled above 1K nodes.',
            },
          })
          return
        }

        const expandButton = root.querySelector<HTMLButtonElement>('button[aria-label="Expand all"]')
        if (!expandButton) {
          throw new Error('GraphMap did not expose its Expand all control.')
        }
        const expansionStarted = performance.now()
        actionStartMilliseconds = expansionStarted
        expandButton.click()
        terminal = await observeStableGraph(root, {
          startMilliseconds: expansionStarted,
          minimumNodeCount: activeGraph.nodes.length,
          requireAnEdge: activeGraph.edges.length > 0,
        })
        recordBoundary(
          'graph-map',
          'dagre-layout',
          terminal.startMilliseconds,
          terminal.endMilliseconds,
          'estimated',
          'consumer-stable-dom-estimate',
          {
            action: 'full-expansion',
            stableFrameCount: terminal.stableFrameCount,
            renderedNodeCount: terminal.snapshot.renderedNodeCount,
            renderedEdgeCount: terminal.snapshot.renderedEdgeCount,
            limitation: 'Aggregate expansion-to-stable estimate; no internal layout callback is exposed.',
          },
        )
        recordBoundary(
          'graph-map',
          'viewport-fit',
          terminal.firstNodeMilliseconds,
          terminal.endMilliseconds,
          'estimated',
          'consumer-animation-frame-settle',
          {
            action: 'full-expansion',
            stableFrameCount: terminal.stableFrameCount,
            viewportTransform: terminal.snapshot.viewportTransform,
          },
        )
      } else if (config.action === 'search') {
        if (!config.searchQuery || config.searchQuery.length < 3) {
          throw new Error('Search journeys require a query of at least three characters.')
        }
        const searchButton = root.querySelector<HTMLButtonElement>('button[aria-label="Search"]')
        if (!searchButton) {
          throw new Error('GraphMap did not expose its Search graph control.')
        }
        searchButton.click()
        await nextAnimationFrame()
        const searchInput = root.querySelector<HTMLInputElement>(
          `input[placeholder="${GRAPHMAP_SEARCH_PLACEHOLDER}"]`,
        )
        if (!searchInput) {
          throw new Error('GraphMap search opened without its query input.')
        }
        const searchStarted = performance.now()
        actionStartMilliseconds = searchStarted
        setReactInputValue(searchInput, config.searchQuery)
        const search = await waitForSearchStatus(root)
        recordBoundary(
          'browser-data',
          'search-completion',
          searchStarted,
          search.endMilliseconds,
          'externally-observed',
          'graphmap-existing-search-dom-status',
          {
            query: config.searchQuery,
            statusText: search.statusText,
            searchStatus: search.statusText,
            matchCount: search.matchCount,
            requiredAncestorUnionCount: search.requiredAncestorUnionCount,
            totalResultCardinality: search.totalResultCardinality,
            limitation: 'Input action to GraphMap DOM status includes the package-owned debounce; no internal search callback is exposed.',
          },
        )
        searchStatusText = search.statusText
        matchCount = search.matchCount
        requiredAncestorUnionCount = search.requiredAncestorUnionCount
        totalResultCardinality = search.totalResultCardinality
        terminal = await observeStableGraph(root, {
          startMilliseconds: search.endMilliseconds,
          minimumNodeCount: search.requiredAncestorUnionCount,
          requireAnEdge: search.requiredAncestorUnionCount > 1,
        })
        const renderedUnionNodeIds = search.matchCount > 0
          ? terminal.snapshot.nodeIds
          : []
        if (search.matchCount > 0 &&
            renderedUnionNodeIds.length !== search.requiredAncestorUnionCount) {
          throw new Error(
            `GraphMap reported ${search.requiredAncestorUnionCount} total shown nodes but the stable DOM exposed ` +
            `${renderedUnionNodeIds.length} visible-union node IDs.`,
          )
        }
        requiredAncestorNodeIds = renderedUnionNodeIds
        if (search.matchCount > 0) {
          recordBoundary(
            'graph-map',
            'dagre-layout',
            search.endMilliseconds,
            terminal.endMilliseconds,
            'estimated',
            'consumer-stable-dom-estimate',
            {
              action: 'search',
              statusText: search.statusText,
              stableFrameCount: terminal.stableFrameCount,
              renderedUnionNodeIds,
              limitation: 'Aggregate search-status-to-stable estimate; GraphMap exposes no internal Dagre lifecycle callback.',
            },
          )
        }
        recordBoundary(
          'graph-map',
          'viewport-fit',
          search.endMilliseconds,
          terminal.endMilliseconds,
          'estimated',
          'consumer-animation-frame-settle',
          {
            action: 'search',
            statusText: search.statusText,
            stableFrameCount: terminal.stableFrameCount,
            viewportTransform: terminal.snapshot.viewportTransform,
            renderedUnionNodeIds,
            limitation: 'These are the complete visible-union DOM IDs, not match-only or ancestor-only IDs.',
          },
        )
      }

      recordSelectedGraphReactCommit(
        config.action,
        actionStartMilliseconds,
        terminal.endMilliseconds,
      )

      complete('succeeded', {
        completedAtEpochMilliseconds: Date.now(),
        completedAtMonotonicMilliseconds: terminal.endMilliseconds,
        stableSelector: BROWSER_JOURNEY_RESULT_SELECTOR,
        stableState: 'stable-graph-view',
        stableFrameCount: terminal.stableFrameCount,
        viewportTransform: terminal.snapshot.viewportTransform,
        searchStatus: searchStatusText,
        notes: [
          'GraphMap 0.2.0 is measured only through consumer instrumentation and observable DOM state.',
          'No ResizeObserver message is suppressed; the driver treats unexpected page and console errors as failures.',
        ],
      }, {
        renderedNodeCount: terminal.snapshot.renderedNodeCount,
        renderedEdgeCount: terminal.snapshot.renderedEdgeCount,
        matchCount,
        requiredAncestorUnionCount,
        requiredAncestorNodeIds,
        totalResultCardinality,
        identityLimitation: config.action === 'search'
          ? 'GraphMap exposes match/required-union counts and visible union IDs, but not exact match IDs; matchNodeIds remains null. The response-byte SHA is not a canonical dataset fingerprint.'
          : 'responsePayloadSha256 hashes exact response bytes and is not the canonical dataset fingerprint.',
      })
    }

    void runGraphJourney().catch(fail)
    return () => {
      active = false
    }
  }, [complete, config, fail, graph, recordBoundary, recordSelectedGraphReactCommit])

  useEffect(() => {
    if (config.action !== 'result-render' || !resultPayload || completedRef.current) {
      return
    }

    const activePayload = resultPayload
    let active = true
    async function completeResultRender() {
      await nextAnimationFrame()
      await nextAnimationFrame()
      if (!active) return
      const completedAt = performance.now()
      complete('succeeded', {
        completedAtEpochMilliseconds: Date.now(),
        completedAtMonotonicMilliseconds: completedAt,
        stableSelector: '[data-testid="bounded-analysis-result"]',
        stableState: 'stable-bounded-result',
        stableFrameCount: 2,
        viewportTransform: null,
        searchStatus: null,
        notes: [
          'Only bounded rows and textual paths are mounted; complete cardinality and digest remain visible.',
          'No source-graph canvas is required to present a complete algorithm-result identity.',
        ],
      }, {
        totalResultCardinality: activePayload.totalResultCardinality,
        boundedResultItemCount: resultPayloadItemCount(activePayload),
        identityLimitation: null,
      })
    }

    void completeResultRender().catch(fail)
    return () => {
      active = false
    }
  }, [complete, config.action, fail, resultPayload])

  return (
    <div className="insights-browser-harness" ref={rootRef}>
      {config.action === 'result-render' && resultPayload ? (
        <Profiler id="bounded-analysis-result" onRender={onResultRender}>
          <BoundedAnalysisResult payload={resultPayload} />
        </Profiler>
      ) : graph ? (
        <div className="insights-browser-harness__graph">
          <Profiler id="insights-graph-map" onRender={onGraphRender}>
            <InsightsGraphCanvas
              graph={graph}
              onNodeSelect={() => undefined}
              isFullscreen={isFullscreen}
              onFullscreenChange={setIsFullscreen}
              onGraphMapAdapterMeasured={onGraphMapAdapterMeasured}
            />
          </Profiler>
        </div>
      ) : (
        <p className="insights-browser-harness__status">Preparing browser journey…</p>
      )}
      <output
        className="insights-browser-harness__contract"
        data-state={terminalResult ? 'completed' : 'running'}
        data-testid="insights-browser-benchmark-result"
      >
        {terminalResult ? JSON.stringify(terminalResult) : 'running'}
      </output>
    </div>
  )
}
