import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import type { GraphFixture, GraphFixtureNode } from '../../fixtures/sampleGraph'
import {
  getBoundedNodeCounterSet,
  getEvidenceImpactRanking,
  getGraphBySlug,
  getGraphCatalog,
  getLeastRobustNode,
  getNodeCounterSet,
  getNodeRobustnessRanking,
  updateNode,
  type GraphDataSource,
} from '../../services/graphService'
import {
  createBenchmarkSet,
  getPerformanceRuns,
  type BenchmarkSet,
  type PerformanceReportDocument,
  type PerformanceRunRecord,
} from '../../services/performanceRuns'
import { STRESS_GRAPH_OPTIONS, type StressGraphId } from '../../services/stressGraphs'
import { InsightsLabTrends } from './InsightsLabTrends'
import './InsightsLabDialog.css'

export interface InsightsLabDialogProps {
  isOpen: boolean
  graph: GraphFixture | null
  graphDataSource: GraphDataSource
  installedStressGraphIds?: readonly StressGraphId[]
  onClose: () => void
  onGraphUpdated?: () => void
}

type LabTab = 'run' | 'history' | 'trends'
type OperationId = 'minimal' | 'bounded' | 'evidence' | 'least' | 'ranking' | 'leaf'

type SuiteProgress = {
  current: number
  total: number
  graphLabel?: string
  graphSlug?: string
  operationTitle?: string
}

type SuiteFailure = {
  graphLabel: string
  graphSlug: string
  operationTitle: string
}

type SuiteSummary = {
  status: 'completed' | 'stopped' | 'empty'
  total: number
  completed: number
  timedOut: number
  failed: number
  interrupted: number
  graphCount: number
  failures: readonly SuiteFailure[]
}

type OperationDefinition = {
  id: OperationId
  title: string
  description: string
  explanation: string
  caveat: string
  algorithmName: string
  implementation?: string
  requiresTarget?: boolean
  databaseOnly?: boolean
  cancellable?: boolean
}

type OperationInvocationResult = {
  runNumber?: number
  status?: 'completed' | 'timedOut'
}

const OPERATIONS: readonly OperationDefinition[] = [
  {
    id: 'minimal',
    title: 'Minimal counter set',
    description: 'Run the greedy counter-set search for the graph root.',
    explanation: 'Quickly looks for a small group of existing counterarguments that would lower the graph root below the Lab\'s confidence cutoff. It considers the most promising counters first.',
    caveat: 'This is a fast search, not a proof: a smaller counter set may exist.',
    algorithmName: 'minimal-counter-set',
    implementation: 'greedy',
    requiresTarget: true,
    cancellable: true,
  },
  {
    id: 'bounded',
    title: 'Time-bounded exhaustive search',
    description: 'Search combinations drawn from every eligible counter, with a fixed two-minute compute budget.',
    explanation: 'Tests counterargument combinations from smallest to largest. When it finds a qualifying set, every smaller size has already been exhausted, so the returned size is proven minimal.',
    caveat: 'This is the Lab\'s exhaustive reference implementation, not a claim that every counter-set method must enumerate combinations. If the server\'s two-minute budget expires, the partial result shows how far this implementation searched without claiming minimum-set proof.',
    algorithmName: 'minimal-counter-set',
    implementation: 'time-bounded-exhaustive',
    requiresTarget: true,
    cancellable: true,
  },
  {
    id: 'evidence',
    title: 'Evidence impact ranking',
    description: 'Rank evidence by its impact on the graph root.',
    explanation: 'Estimates how much each supporting or opposing piece of evidence affects the graph root by calculating what its confidence would be without that evidence. It separates supporting and counter evidence, then ranks each group by the predicted change.',
    caveat: 'This measures sensitivity within the model, not whether a piece of evidence is true or caused the outcome.',
    algorithmName: 'evidence-impact-ranking',
    requiresTarget: true,
  },
  {
    id: 'least',
    title: 'Least robust node',
    description: 'Find the least robust node in the active graph.',
    explanation: 'Checks every node by removing each downstream piece of supporting or opposing evidence one at a time, recalculating the node with the Bayesian-factor model, and finding its largest confidence change. It returns the node with the largest such change.',
    caveat: 'A lower robustness score means the node is more sensitive to one-at-a-time evidence removal within this model. It does not judge whether the node is true or well supported outside the graph.',
    algorithmName: 'least-robust-node',
    cancellable: true,
  },
  {
    id: 'ranking',
    title: 'Robustness ranking',
    description: 'Rank every node in the active graph by robustness.',
    explanation: 'Applies the same one-at-a-time downstream evidence test to every node, recalculates each result with the Bayesian-factor model, and orders the nodes from most sensitive to least sensitive.',
    caveat: 'This measures sensitivity to removing one supporting or opposing evidence node, not the overall quality or truth of a node.',
    algorithmName: 'robustness-ranking',
    cancellable: true,
  },
  {
    id: 'leaf',
    title: 'Leaf update',
    description: 'Reapply prior odds to the ordinal-highest node and measure the update path.',
    explanation: 'Exercises the real database update and recalculation path without intentionally changing the graph. It writes the highest-ID node\'s existing prior odds back to that node, recalculates that node and its affected ancestors, and saves the results.',
    caveat: 'The highest-ID node is treated as a likely leaf but is not guaranteed to be one. This operation is unavailable for fixture graphs.',
    algorithmName: 'leaf-update',
    databaseOnly: true,
  },
]

const FOCUSABLE_SELECTOR = [
  'button:not(:disabled):not([tabindex="-1"])',
  'input:not(:disabled):not([tabindex="-1"])',
  'select:not(:disabled):not([tabindex="-1"])',
  'textarea:not(:disabled):not([tabindex="-1"])',
  '[href]:not([tabindex="-1"])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

const CANCEL_REPORT_POLL_ATTEMPTS = 8
const CANCEL_REPORT_POLL_INTERVAL_MS = 250
const CANONICAL_STRESS_TARGET_ID = 'n-00000'
const BENCHMARK_SET_STORAGE_KEY = 'insights-lab-benchmark-set-id'
const STANDARD_STRESS_GRAPH_OPTIONS = STRESS_GRAPH_OPTIONS.filter(
  ({ id }) => !id.startsWith('stress-deep-'),
)

function sortRuns(document: PerformanceReportDocument): PerformanceRunRecord[] {
  return [...(Array.isArray(document.runs) ? document.runs : [])]
    .sort((left, right) => right.runNumber - left.runNumber)
}

function getHighestOrdinalNode(nodes: readonly GraphFixtureNode[]): GraphFixtureNode | undefined {
  return nodes.reduce<GraphFixtureNode | undefined>((highest, node) => (
    highest === undefined || node.id > highest.id ? node : highest
  ), undefined)
}

function compareNodeIds(left: GraphFixtureNode, right: GraphFixtureNode): number {
  return left.id < right.id ? -1 : left.id > right.id ? 1 : 0
}

function getCanonicalRootNode(graph: GraphFixture): GraphFixtureNode | undefined {
  return graph.nodes
    .filter(({ kind }) => kind === 'root')
    .sort(compareNodeIds)[0]
}

async function invokeOperation({
  benchmarkSetId,
  dataSource,
  graphSlug,
  leafTargetNode,
  operation,
  signal,
  targetNodeId,
}: {
  benchmarkSetId: string
  dataSource: GraphDataSource
  graphSlug: string
  leafTargetNode?: GraphFixtureNode
  operation: OperationDefinition
  signal?: AbortSignal
  targetNodeId?: string
}): Promise<OperationInvocationResult> {
  switch (operation.id) {
    case 'minimal':
      await getNodeCounterSet(
        graphSlug,
        targetNodeId!,
        dataSource,
        signal,
        benchmarkSetId,
      )
      return {}
    case 'bounded': {
      const result = await getBoundedNodeCounterSet(
        graphSlug,
        targetNodeId!,
        dataSource,
        signal,
        benchmarkSetId,
      )
      return { runNumber: result.runNumber, status: result.status }
    }
    case 'evidence':
      await getEvidenceImpactRanking(
        graphSlug,
        targetNodeId!,
        dataSource,
        signal,
        benchmarkSetId,
      )
      return {}
    case 'least':
      await getLeastRobustNode(
        graphSlug,
        dataSource,
        signal,
        benchmarkSetId,
      )
      return {}
    case 'ranking':
      await getNodeRobustnessRanking(
        graphSlug,
        dataSource,
        signal,
        benchmarkSetId,
      )
      return {}
    case 'leaf':
      await updateNode(
        graphSlug,
        leafTargetNode!.id,
        { priorOdds: leafTargetNode!.priorOdds },
        benchmarkSetId,
      )
      return {}
  }
}

function getOperationTime(run: PerformanceRunRecord): number | null {
  const value = run.timing?.operationElapsedMilliseconds ?? run.timing?.operationElapsedMs
  return typeof value === 'number' ? value : null
}

function formatMilliseconds(value: number | null): string {
  if (value === null) return '—'
  return `${value.toLocaleString(undefined, { maximumFractionDigits: 2 })} ms`
}

function formatDateTime(value?: string): string {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? value : date.toLocaleString()
}

function sentenceCase(value?: string): string {
  if (!value) return 'Unknown'
  const spaced = value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[-_]/g, ' ')
  return `${spaced.charAt(0).toUpperCase()}${spaced.slice(1)}`
}

function operationLabel(run: PerformanceRunRecord): string {
  const name = run.algorithm?.name
  if (name === 'minimal-counter-set') {
    if (run.algorithm?.implementation === 'time-bounded-exhaustive') {
      return 'Time-bounded exhaustive search'
    }
    if (run.algorithm?.implementation === 'bounded-brute-force') {
      return 'Bounded minimal counter set'
    }
    return 'Minimal counter set'
  }
  return ({
    'evidence-impact-ranking': 'Evidence impact ranking',
    'least-robust-node': 'Least robust node',
    'robustness-ranking': 'Robustness ranking',
    'leaf-update': 'Leaf update',
  } as Record<string, string>)[name ?? ''] ?? sentenceCase(name)
}

function benchmarkSetName(
  benchmarkSetId: string | null | undefined,
  benchmarkSets: readonly BenchmarkSet[],
): string {
  if (!benchmarkSetId) return 'Unassigned'
  const benchmarkSet = benchmarkSets.find(({ id }) => id === benchmarkSetId)
  if (!benchmarkSet) return 'Unknown benchmark set'
  const normalizedName = benchmarkSet.name.trim().toLowerCase()
  const hasDuplicateName = benchmarkSets.some((candidate) => (
    candidate.id !== benchmarkSet.id
    && candidate.name.trim().toLowerCase() === normalizedName
  ))
  return hasDuplicateName
    ? `${benchmarkSet.name} · ${benchmarkSet.id.slice(-6)}`
    : benchmarkSet.name
}

function proofStatus(run: PerformanceRunRecord): string | undefined {
  const detailsProof = run.details?.proofStatus
  const outcomeProof = run.outcome?.proofStatus
  const raw = typeof detailsProof === 'string'
    ? detailsProof
    : typeof outcomeProof === 'string'
      ? outcomeProof
      : run.outcome?.status === 'notProven'
        ? 'notProven'
        : undefined
  if (raw === 'notProven') return 'Not proven'
  if (raw === 'notApplicable') return undefined
  return raw ? sentenceCase(raw) : undefined
}

function executionStatus(run: PerformanceRunRecord): string {
  if (run.outcome?.status === 'notProven') return 'Completed'
  if (run.outcome?.status === 'timedOut') return 'Timed out'
  return sentenceCase(run.outcome?.status)
}

function presentedOutcome(run: PerformanceRunRecord): Record<string, unknown> | undefined {
  if (!run.outcome) return undefined
  const proof = proofStatus(run)
  return {
    ...run.outcome,
    status: executionStatus(run),
    ...(proof ? { proofStatus: proof } : {}),
  }
}

function runMatches(
  run: PerformanceRunRecord,
  operation: OperationDefinition,
  graphSlug: string,
  graphDataSource: GraphDataSource,
  benchmarkSetId: string,
  targetNodeId?: string,
): boolean {
  if (run.algorithm?.name !== operation.algorithmName || run.graph?.slug !== graphSlug) return false
  if (run.invocation?.dataSource !== graphDataSource) return false
  if (run.benchmarkSetId !== benchmarkSetId) return false
  if (operation.implementation && run.algorithm?.implementation !== operation.implementation) return false
  if (operation.requiresTarget && run.invocation?.targetNodeId !== targetNodeId) return false
  if (operation.id === 'leaf' && run.invocation?.changedNodeId !== targetNodeId) return false
  return true
}

type OpenDialogProps = Omit<InsightsLabDialogProps, 'isOpen'>

export function InsightsLabDialog({ isOpen, ...props }: InsightsLabDialogProps) {
  return isOpen ? <OpenInsightsLabDialog {...props} /> : null
}

function OpenInsightsLabDialog({
  graph,
  graphDataSource,
  installedStressGraphIds,
  onClose,
  onGraphUpdated,
}: OpenDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef<HTMLButtonElement>(null)
  const runTabRef = useRef<HTMLButtonElement>(null)
  const historyTabRef = useRef<HTMLButtonElement>(null)
  const trendsTabRef = useRef<HTMLButtonElement>(null)
  const historyBackRef = useRef<HTMLButtonElement>(null)
  const runViewButtonRefs = useRef(new Map<number, HTMLButtonElement>())
  const runningStatusRef = useRef<HTMLDivElement>(null)
  const suiteResultRef = useRef<HTMLDivElement>(null)
  const suiteButtonRef = useRef<HTMLButtonElement>(null)
  const focusHistoryDetailRef = useRef(false)
  const focusRunListItemRef = useRef<number | undefined>(undefined)
  const focusSuiteResultRef = useRef(false)
  const abortControllerRef = useRef<AbortController | null>(null)
  const runGuardRef = useRef(false)
  const suiteStopRequestedRef = useRef(false)
  const [tab, setTab] = useState<LabTab>('run')
  const [expandedOperationId, setExpandedOperationId] = useState<OperationId>()
  const [runs, setRuns] = useState<PerformanceRunRecord[]>([])
  const [benchmarkSets, setBenchmarkSets] = useState<BenchmarkSet[]>([])
  const [selectedBenchmarkSetId, setSelectedBenchmarkSetId] = useState('')
  const [newBenchmarkSetName, setNewBenchmarkSetName] = useState('')
  const [isCreatingBenchmarkSet, setIsCreatingBenchmarkSet] = useState(false)
  const [benchmarkSetError, setBenchmarkSetError] = useState<string | null>(null)
  const [selectedRunNumber, setSelectedRunNumber] = useState<number>()
  const [isHistoryLoading, setIsHistoryLoading] = useState(true)
  const [historyError, setHistoryError] = useState<string | null>(null)
  const [runError, setRunError] = useState<string | null>(null)
  const [runNotice, setRunNotice] = useState<string | null>(null)
  const [activeOperation, setActiveOperation] = useState<OperationDefinition | null>(null)
  const [isCancellationRequested, setIsCancellationRequested] = useState(false)
  const [isFinalizingRun, setIsFinalizingRun] = useState(false)
  const [isSuiteRunning, setIsSuiteRunning] = useState(false)
  const [suiteProgress, setSuiteProgress] = useState<SuiteProgress | null>(null)
  const [suiteSummary, setSuiteSummary] = useState<SuiteSummary | null>(null)

  const highestNode = useMemo(
    () => graph ? getHighestOrdinalNode(graph.nodes) : undefined,
    [graph],
  )
  const algorithmTargetNode = useMemo(
    () => graph ? getCanonicalRootNode(graph) : undefined,
    [graph],
  )
  const algorithmTargetNodeId = algorithmTargetNode?.id
  const isBusy = activeOperation !== null || isSuiteRunning
  const selectedRun = runs.find(({ runNumber }) => runNumber === selectedRunNumber)
  const knownInstalledStandardStressGraphs = useMemo(() => {
    if (installedStressGraphIds === undefined) return undefined
    const installedIds = new Set<string>(installedStressGraphIds)
    return STANDARD_STRESS_GRAPH_OPTIONS.filter(({ id }) => installedIds.has(id))
  }, [installedStressGraphIds])

  const receiveBenchmarkSets = (nextSets: BenchmarkSet[]) => {
    setBenchmarkSets(nextSets)
    setSelectedBenchmarkSetId((current) => {
      if (nextSets.some(({ id }) => id === current)) return current
      const stored = window.localStorage.getItem(BENCHMARK_SET_STORAGE_KEY)
      if (stored && nextSets.some(({ id }) => id === stored)) return stored
      return nextSets.length === 1 ? nextSets[0].id : ''
    })
  }

  useEffect(() => {
    if (selectedBenchmarkSetId) {
      window.localStorage.setItem(BENCHMARK_SET_STORAGE_KEY, selectedBenchmarkSetId)
    }
  }, [selectedBenchmarkSetId])

  useEffect(() => {
    let isActive = true
    const previousFocus = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null
    const isolatedElements: Array<{
      element: HTMLElement
      hadInert: boolean
      previousAriaHidden: string | null
    }> = []

    let modalBranch: HTMLElement | null = dialogRef.current
    while (modalBranch?.parentElement) {
      const parent: HTMLElement = modalBranch.parentElement
      for (const sibling of Array.from(parent.children)) {
        if (sibling === modalBranch || !(sibling instanceof HTMLElement)) continue
        isolatedElements.push({
          element: sibling,
          hadInert: sibling.hasAttribute('inert'),
          previousAriaHidden: sibling.getAttribute('aria-hidden'),
        })
        sibling.setAttribute('inert', '')
        sibling.setAttribute('aria-hidden', 'true')
      }
      modalBranch = parent
      if (parent === document.body) break
    }

    closeRef.current?.focus()

    getPerformanceRuns()
      .then((document) => {
        if (!isActive) return
        const nextRuns = sortRuns(document)
        setRuns(nextRuns)
        receiveBenchmarkSets(Array.isArray(document.benchmarkSets) ? document.benchmarkSets : [])
        setSelectedRunNumber((current) => (
          nextRuns.some(({ runNumber }) => runNumber === current)
            ? current
            : undefined
        ))
        setHistoryError(null)
      })
      .catch(() => {
        if (isActive) setHistoryError('Unable to load performance run history.')
      })
      .finally(() => {
        if (isActive) setIsHistoryLoading(false)
      })

    return () => {
      isActive = false
      suiteStopRequestedRef.current = true
      abortControllerRef.current?.abort()
      for (const { element, hadInert, previousAriaHidden } of isolatedElements) {
        if (!hadInert) element.removeAttribute('inert')
        if (previousAriaHidden === null) element.removeAttribute('aria-hidden')
        else element.setAttribute('aria-hidden', previousAriaHidden)
      }
      previousFocus?.focus()
    }
  }, [])

  useEffect(() => {
    if (isBusy) runningStatusRef.current?.focus()
  }, [isBusy])

  useEffect(() => {
    if (isBusy || !focusSuiteResultRef.current) return
    focusSuiteResultRef.current = false
    const focusTarget = suiteResultRef.current ?? suiteButtonRef.current
    focusTarget?.focus()
  }, [isBusy, suiteSummary])

  useEffect(() => {
    if (tab !== 'history' || selectedRunNumber === undefined || !focusHistoryDetailRef.current) return
    focusHistoryDetailRef.current = false
    historyBackRef.current?.focus()
  }, [selectedRunNumber, tab])

  useEffect(() => {
    if (tab !== 'history' || selectedRunNumber !== undefined || focusRunListItemRef.current === undefined) return
    const runNumber = focusRunListItemRef.current
    focusRunListItemRef.current = undefined
    runViewButtonRefs.current.get(runNumber)?.focus()
  }, [selectedRunNumber, tab])

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      event.stopPropagation()
      if (expandedOperationId) {
        setExpandedOperationId(undefined)
        return
      }
      if (!runGuardRef.current) onClose()
      return
    }
    if (event.key !== 'Tab') return
    const elements = Array.from(
      dialogRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR) ?? [],
    )
    if (elements.length === 0) {
      event.preventDefault()
      return
    }
    const first = elements[0]
    const last = elements[elements.length - 1]
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  const handleTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    const tabs: readonly LabTab[] = ['run', 'history', 'trends']
    const currentIndex = tabs.indexOf(tab)
    let nextTab: LabTab | undefined
    if (event.key === 'ArrowLeft') {
      nextTab = tabs[(currentIndex - 1 + tabs.length) % tabs.length]
    } else if (event.key === 'ArrowRight') {
      nextTab = tabs[(currentIndex + 1) % tabs.length]
    } else if (event.key === 'Home') {
      nextTab = tabs[0]
    } else if (event.key === 'End') {
      nextTab = tabs[tabs.length - 1]
    }
    if (!nextTab) return
    event.preventDefault()
    setTab(nextTab)
    if (nextTab !== 'run') setExpandedOperationId(undefined)
    const nextTabRef = {
      run: runTabRef,
      history: historyTabRef,
      trends: trendsTabRef,
    }[nextTab]
    nextTabRef.current?.focus()
  }

  const handleCreateBenchmarkSet = async () => {
    const name = newBenchmarkSetName.trim()
    if (!name || isCreatingBenchmarkSet || isBusy) return
    setIsCreatingBenchmarkSet(true)
    setBenchmarkSetError(null)
    try {
      const created = await createBenchmarkSet(name)
      setBenchmarkSets((current) => (
        current.some(({ id }) => id === created.id)
          ? current.map((benchmarkSet) => benchmarkSet.id === created.id ? created : benchmarkSet)
          : [...current, created]
      ))
      setSelectedBenchmarkSetId(created.id)
      setNewBenchmarkSetName('')
    } catch {
      setBenchmarkSetError('Unable to create the benchmark set.')
    } finally {
      setIsCreatingBenchmarkSet(false)
    }
  }

  const launch = async (operation: OperationDefinition) => {
    if (runGuardRef.current || !graph || !selectedBenchmarkSetId) return
    const benchmarkSetId = selectedBenchmarkSetId
    const leafTargetNode = operation.id === 'leaf' ? highestNode : undefined
    const targetNodeId = operation.id === 'leaf' ? leafTargetNode?.id : algorithmTargetNodeId
    if (operation.requiresTarget && !targetNodeId) return
    if (operation.id === 'leaf' && !leafTargetNode) return
    if (operation.databaseOnly && graphDataSource !== 'database') return

    runGuardRef.current = true
    setExpandedOperationId(undefined)
    setActiveOperation(operation)
    setRunError(null)
    setRunNotice(null)
    setIsCancellationRequested(false)
    setIsFinalizingRun(false)
    const controller = operation.cancellable ? new AbortController() : null
    abortControllerRef.current = controller
    let watermark = -1
    let exactRunNumber: number | undefined
    let invocationStatus: 'completed' | 'timedOut' | undefined
    let launchFailure: unknown
    let requestStarted = false
    let requestCompleted = false

    try {
      const beforeDocument = await getPerformanceRuns()
      const before = sortRuns(beforeDocument)
      setRuns(before)
      receiveBenchmarkSets(Array.isArray(beforeDocument.benchmarkSets) ? beforeDocument.benchmarkSets : [])
      watermark = before.reduce((maximum, run) => Math.max(maximum, run.runNumber), -1)
      if (controller?.signal.aborted) throw new DOMException('Aborted', 'AbortError')

      try {
        requestStarted = true
        const invocationResult = await invokeOperation({
          benchmarkSetId,
          dataSource: graphDataSource,
          graphSlug: graph.slug,
          leafTargetNode,
          operation,
          signal: controller?.signal,
          targetNodeId,
        })
        exactRunNumber = invocationResult.runNumber
        invocationStatus = invocationResult.status
        if (operation.id === 'leaf') onGraphUpdated?.()
        requestCompleted = true
      } catch (error) {
        launchFailure = error
      }

      setIsFinalizingRun(true)
      const wasCancelled = controller?.signal.aborted === true
      const attempts = wasCancelled ? CANCEL_REPORT_POLL_ATTEMPTS : 1
      let after: PerformanceRunRecord[] = []
      let matchingRun: PerformanceRunRecord | undefined
      for (let attempt = 0; attempt < attempts; attempt += 1) {
        const afterDocument = await getPerformanceRuns()
        after = sortRuns(afterDocument)
        setRuns(after)
        receiveBenchmarkSets(Array.isArray(afterDocument.benchmarkSets) ? afterDocument.benchmarkSets : [])
        setHistoryError(null)
        matchingRun = exactRunNumber === undefined
          ? after.find((run) => (
            run.runNumber > watermark
            && runMatches(
              run,
              operation,
              graph.slug,
              graphDataSource,
              benchmarkSetId,
              targetNodeId,
            )
          ))
          : after.find((run) => (
            run.runNumber === exactRunNumber && run.benchmarkSetId === benchmarkSetId
          ))
        if (matchingRun || attempt === attempts - 1) break
        await new Promise((resolve) => setTimeout(resolve, CANCEL_REPORT_POLL_INTERVAL_MS))
      }

      if (matchingRun) {
        setSelectedRunNumber(matchingRun.runNumber)
        focusHistoryDetailRef.current = true
        setTab('history')
      }

      if (launchFailure) {
        if (controller?.signal.aborted) {
          setRunNotice(matchingRun ? 'Run cancelled. Its report is shown below.' : 'Run cancelled.')
        } else {
          setRunError(matchingRun
            ? 'The run did not complete successfully. Its report is shown below.'
            : 'The algorithm run failed before a report could be loaded.')
        }
      } else if (!matchingRun) {
        setRunError('The run finished, but its report could not be matched in history.')
      } else if (invocationStatus === 'timedOut' || matchingRun.outcome?.status === 'timedOut') {
        setRunNotice('The exhaustive search reached its two-minute compute budget. Its partial report is shown below.')
      }
    } catch {
      if (controller?.signal.aborted) {
        setRunNotice(requestStarted
          ? 'Cancellation was requested, but no stored report could be confirmed.'
          : 'Run cancelled before it started.')
      } else if (requestCompleted) {
        setRunError('The run completed, but its report could not be loaded.')
      } else if (requestStarted) {
        setRunError('The algorithm run failed, and its report could not be loaded.')
      } else {
        setRunError('Unable to load run history, so the algorithm was not started.')
      }
    } finally {
      abortControllerRef.current = null
      runGuardRef.current = false
      setActiveOperation(null)
      setIsCancellationRequested(false)
      setIsFinalizingRun(false)
    }
  }

  const launchStressSuite = async () => {
    if (runGuardRef.current || !selectedBenchmarkSetId) return

    const benchmarkSetId = selectedBenchmarkSetId
    runGuardRef.current = true
    suiteStopRequestedRef.current = false
    setExpandedOperationId(undefined)
    setSelectedRunNumber(undefined)
    setTab('run')
    setRunError(null)
    setRunNotice(null)
    setSuiteSummary(null)
    setSuiteProgress({ current: 0, total: 0 })
    setIsSuiteRunning(true)
    setIsCancellationRequested(false)
    setIsFinalizingRun(false)

    const suiteWatermark = runs.reduce((maximum, run) => Math.max(maximum, run.runNumber), -1)
    let completed = 0
    let timedOut = 0
    let failed = 0
    let interrupted = 0
    let processed = 0
    let graphCount = 0
    let total = 0
    let didUpdateActiveGraph = false
    const failures: SuiteFailure[] = []
    let interruptedRun: {
      graphSlug: string
      operation: OperationDefinition
      targetNodeId?: string
    } | undefined

    try {
      let installedGraphs = knownInstalledStandardStressGraphs
      if (installedGraphs === undefined) {
        const catalog = await getGraphCatalog()
        const installedIds = new Set(catalog.map(({ slug }) => slug))
        installedGraphs = STANDARD_STRESS_GRAPH_OPTIONS.filter(({ id }) => installedIds.has(id))
      }

      graphCount = installedGraphs.length
      total = graphCount * OPERATIONS.length
      setSuiteProgress({ current: 0, total })

      if (installedGraphs.length === 0) {
        setSuiteSummary({
          status: 'empty',
          total,
          completed,
          timedOut,
          failed,
          interrupted,
          graphCount,
          failures,
        })
        return
      }

      suite: for (const graphOption of installedGraphs) {
        let loadedGraph: GraphFixture | undefined

        for (const operation of OPERATIONS) {
          if (suiteStopRequestedRef.current) break suite

          setActiveOperation(operation)
          setIsCancellationRequested(false)
          setSuiteProgress({
            current: processed + 1,
            total,
            graphLabel: graphOption.label,
            graphSlug: graphOption.id,
            operationTitle: operation.title,
          })

          let countItem = false
          let leafTargetNode: GraphFixtureNode | undefined
          const controller = operation.cancellable ? new AbortController() : null
          abortControllerRef.current = controller

          try {
            if (operation.id === 'leaf') {
              try {
                loadedGraph ??= await getGraphBySlug(graphOption.id, 'database')
              } catch (error) {
                countItem = true
                throw error
              }

              if (suiteStopRequestedRef.current) break suite
              leafTargetNode = getHighestOrdinalNode(loadedGraph.nodes)
              countItem = true
              if (!leafTargetNode) throw new Error('The stress graph has no node to update.')
            } else {
              countItem = true
            }

            const invocationResult = await invokeOperation({
              benchmarkSetId,
              dataSource: 'database',
              graphSlug: graphOption.id,
              leafTargetNode,
              operation,
              signal: controller?.signal,
              targetNodeId: operation.requiresTarget ? CANONICAL_STRESS_TARGET_ID : undefined,
            })
            if (invocationResult.status === 'timedOut') timedOut += 1
            else completed += 1
            if (operation.id === 'leaf' && graph?.slug === graphOption.id) {
              didUpdateActiveGraph = true
            }
          } catch {
            if (controller?.signal.aborted && suiteStopRequestedRef.current) {
              interrupted += 1
              interruptedRun = {
                graphSlug: graphOption.id,
                operation,
                targetNodeId: operation.requiresTarget ? CANONICAL_STRESS_TARGET_ID : leafTargetNode?.id,
              }
            } else {
              failed += 1
              failures.push({
                graphLabel: graphOption.label,
                graphSlug: graphOption.id,
                operationTitle: operation.title,
              })
            }
          } finally {
            if (countItem) processed += 1
            abortControllerRef.current = null
            setActiveOperation(null)
          }

          if (suiteStopRequestedRef.current) break suite
        }
      }

      const stopped = suiteStopRequestedRef.current
        && (processed < total || interrupted > 0)
      setSuiteSummary({
        status: stopped ? 'stopped' : 'completed',
        total,
        completed,
        timedOut,
        failed,
        interrupted,
        graphCount,
        failures,
      })
    } catch {
      setRunError('Unable to load the installed stress-graph catalog, so the suite was not started.')
    } finally {
      setIsFinalizingRun(true)
      try {
        const attempts = interruptedRun ? CANCEL_REPORT_POLL_ATTEMPTS : 1
        for (let attempt = 0; attempt < attempts; attempt += 1) {
          const document = await getPerformanceRuns()
          const nextRuns = sortRuns(document)
          setRuns(nextRuns)
          receiveBenchmarkSets(Array.isArray(document.benchmarkSets) ? document.benchmarkSets : [])
          setHistoryError(null)

          const foundInterruptedReport = !interruptedRun || nextRuns.some((run) => (
            run.runNumber > suiteWatermark
            && runMatches(
              run,
              interruptedRun!.operation,
              interruptedRun!.graphSlug,
              'database',
              benchmarkSetId,
              interruptedRun!.targetNodeId,
            )
          ))
          if (foundInterruptedReport || attempt === attempts - 1) break
          await new Promise((resolve) => setTimeout(resolve, CANCEL_REPORT_POLL_INTERVAL_MS))
        }
      } catch {
        setHistoryError('Unable to refresh performance run history.')
      }
      if (didUpdateActiveGraph) onGraphUpdated?.()
      focusSuiteResultRef.current = true
      setTab('run')
      setSuiteProgress(null)
      setActiveOperation(null)
      setIsFinalizingRun(false)
      setIsCancellationRequested(false)
      setIsSuiteRunning(false)
      abortControllerRef.current = null
      suiteStopRequestedRef.current = false
      runGuardRef.current = false
    }
  }

  return (
    <div
      className="insights-lab-dialog__backdrop"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !runGuardRef.current) onClose()
      }}
    >
      <div
        aria-labelledby="insights-lab-title"
        aria-modal="true"
        className="insights-lab-dialog"
        data-testid="insights-lab-dialog"
        onKeyDown={handleKeyDown}
        ref={dialogRef}
        role="dialog"
      >
        <header className="insights-lab-dialog__header">
          <div className="insights-lab-dialog__header-copy">
            <span className="eyebrow">Performance workspace</span>
            <h2 id="insights-lab-title">Insights Lab</h2>
          </div>
          <button
            aria-label="Close Insights Lab"
            className="insights-lab-dialog__close"
            disabled={isBusy}
            onClick={onClose}
            ref={closeRef}
            type="button"
          >
            Close
          </button>
          <div aria-label="Insights Lab sections" className="insights-lab-dialog__tabs" role="tablist">
            <button
              aria-controls="insights-lab-run-panel"
              aria-selected={tab === 'run'}
              id="insights-lab-run-tab"
              onClick={() => setTab('run')}
              onKeyDown={handleTabKeyDown}
              ref={runTabRef}
              role="tab"
              tabIndex={tab === 'run' ? 0 : -1}
              type="button"
            >
              Run
            </button>
            <button
              aria-controls="insights-lab-history-panel"
              aria-selected={tab === 'history'}
              id="insights-lab-history-tab"
              onClick={() => {
                setExpandedOperationId(undefined)
                setTab('history')
              }}
              onKeyDown={handleTabKeyDown}
              ref={historyTabRef}
              role="tab"
              tabIndex={tab === 'history' ? 0 : -1}
              type="button"
            >
              History <span>{runs.length}</span>
            </button>
            <button
              aria-controls="insights-lab-trends-panel"
              aria-selected={tab === 'trends'}
              id="insights-lab-trends-tab"
              onClick={() => {
                setExpandedOperationId(undefined)
                setTab('trends')
              }}
              onKeyDown={handleTabKeyDown}
              ref={trendsTabRef}
              role="tab"
              tabIndex={tab === 'trends' ? 0 : -1}
              type="button"
            >
              Trends
            </button>
          </div>
        </header>

        {isBusy ? (
          <div
            aria-live="polite"
            className="insights-lab-dialog__running"
            ref={runningStatusRef}
            role="status"
            tabIndex={-1}
          >
            <span className="insights-lab-dialog__spinner" aria-hidden="true" />
            <div>
              <strong>
                {isSuiteRunning
                  ? isFinalizingRun
                    ? 'Finalizing standard stress suite…'
                    : isCancellationRequested
                      ? 'Stopping standard stress suite…'
                      : suiteProgress?.total
                        ? 'Running standard stress suite…'
                        : 'Preparing standard stress suite…'
                  : isFinalizingRun
                    ? 'Finalizing…'
                    : isCancellationRequested
                      ? 'Cancelling…'
                      : 'Running…'}
              </strong>
              <span>
                {isSuiteRunning
                  ? isFinalizingRun
                    ? 'Refreshing run history.'
                    : suiteProgress?.total
                      ? `${suiteProgress.current} of ${suiteProgress.total} · ${suiteProgress.graphLabel ?? suiteProgress.graphSlug ?? 'Stress graph'} · ${suiteProgress.operationTitle ?? 'Starting next operation'}`
                      : 'Finding installed database stress graphs.'
                  : isFinalizingRun
                    ? 'Waiting for the stored report.'
                    : activeOperation?.title}
              </span>
            </div>
            {isSuiteRunning && !isFinalizingRun ? (
              <button
                disabled={isCancellationRequested}
                onClick={() => {
                  suiteStopRequestedRef.current = true
                  setIsCancellationRequested(true)
                  abortControllerRef.current?.abort()
                }}
                type="button"
              >
                Stop suite
              </button>
            ) : activeOperation?.cancellable && !isFinalizingRun ? (
              <button
                disabled={isCancellationRequested}
                onClick={() => {
                  setIsCancellationRequested(true)
                  abortControllerRef.current?.abort()
                }}
                type="button"
              >
                Cancel run
              </button>
            ) : !isSuiteRunning && !isFinalizingRun ? <span>This run cannot be cancelled.</span> : null}
          </div>
        ) : null}
        {runError ? <p className="insights-lab-dialog__error" role="alert">{runError}</p> : null}
        {runNotice ? <p className="insights-lab-dialog__notice" role="status">{runNotice}</p> : null}

        {tab === 'run' ? (
          <section
            aria-labelledby="insights-lab-run-tab"
            className="insights-lab-dialog__panel"
            id="insights-lab-run-panel"
            role="tabpanel"
          >
            <form
              className="insights-lab-dialog__benchmark-sets"
              onSubmit={(event) => {
                event.preventDefault()
                void handleCreateBenchmarkSet()
              }}
            >
              <div>
                <label htmlFor="insights-lab-benchmark-set">Benchmark set</label>
                <select
                  disabled={isBusy || isCreatingBenchmarkSet}
                  id="insights-lab-benchmark-set"
                  onChange={(event) => {
                    setBenchmarkSetError(null)
                    setSelectedBenchmarkSetId(event.target.value)
                  }}
                  value={selectedBenchmarkSetId}
                >
                  <option value="">
                    {benchmarkSets.length === 0 ? 'Create a benchmark set to begin' : 'Select a benchmark set'}
                  </option>
                  {benchmarkSets.map((benchmarkSet) => (
                    <option key={benchmarkSet.id} value={benchmarkSet.id}>
                      {benchmarkSetName(benchmarkSet.id, benchmarkSets)}
                    </option>
                  ))}
                </select>
                <small>
                  {selectedBenchmarkSetId
                    ? 'Every Lab run is recorded in the selected comparison group.'
                    : 'Select or create a benchmark set before running an algorithm.'}
                </small>
              </div>
              <div className="insights-lab-dialog__benchmark-create">
                <label htmlFor="insights-lab-new-benchmark-set">New benchmark set</label>
                <div>
                  <input
                    disabled={isBusy || isCreatingBenchmarkSet}
                    id="insights-lab-new-benchmark-set"
                    onChange={(event) => {
                      setBenchmarkSetError(null)
                      setNewBenchmarkSetName(event.target.value)
                    }}
                    placeholder="Example: LL-699 baseline"
                    type="text"
                    value={newBenchmarkSetName}
                  />
                  <button
                    disabled={!newBenchmarkSetName.trim() || isBusy || isCreatingBenchmarkSet}
                    type="submit"
                  >
                    {isCreatingBenchmarkSet ? 'Creating…' : 'Create and select'}
                  </button>
                </div>
              </div>
              {benchmarkSetError ? <p role="alert">{benchmarkSetError}</p> : null}
            </form>
            <div className="insights-lab-dialog__context">
              <div>
                <span>Active {graphDataSource} graph</span>
                <strong>{graph ? `${graph.title} · ${graph.slug}` : 'No active graph'}</strong>
              </div>
              <div>
                <span>Canonical algorithm target</span>
                <strong>
                  {algorithmTargetNode
                    ? `Root (${algorithmTargetNode.id})`
                    : 'This graph does not contain a root node'}
                </strong>
                <small>Counter-set and evidence-impact runs always use the graph root.</small>
              </div>
            </div>
            <section
              aria-labelledby="insights-lab-stress-suite-title"
              className="insights-lab-dialog__suite-action"
            >
              <div>
                <h3 id="insights-lab-stress-suite-title">Run standard stress suite</h3>
                <p>
                  Run all six Lab operations, one at a time, on
                  {' '}
                  {knownInstalledStandardStressGraphs === undefined
                    ? 'every installed balanced, wide, and shared-diamond database stress graph'
                    : `${knownInstalledStandardStressGraphs.length} installed standard database stress ${knownInstalledStandardStressGraphs.length === 1 ? 'graph' : 'graphs'} (${knownInstalledStandardStressGraphs.length * OPERATIONS.length} planned ${knownInstalledStandardStressGraphs.length * OPERATIONS.length === 1 ? 'run' : 'runs'})`}.
                </p>
                <small>Deep-chain graphs are excluded; manual deep-chain runs can be extremely slow or terminate the backend. No warm-up runs are included.</small>
                <small>The exhaustive reference search can use its full two-minute server compute budget on each graph; reaching that budget is recorded as an expected partial result and the suite continues.</small>
              </div>
              <button
                disabled={isBusy
                  || isHistoryLoading
                  || !selectedBenchmarkSetId
                  || knownInstalledStandardStressGraphs?.length === 0}
                onClick={() => void launchStressSuite()}
                ref={suiteButtonRef}
                type="button"
              >
                Run standard stress suite
              </button>
            </section>
            {suiteSummary ? (
              <div
                className="insights-lab-dialog__suite-summary"
                ref={suiteResultRef}
                role="status"
                tabIndex={-1}
              >
                <strong>
                  {suiteSummary.status === 'completed'
                    ? 'Standard stress suite complete'
                    : suiteSummary.status === 'stopped'
                      ? 'Standard stress suite stopped'
                      : 'No standard stress graphs found'}
                </strong>
                {suiteSummary.status === 'empty' ? (
                  <span>Install at least one balanced, wide, or shared-diamond database stress graph to run the suite.</span>
                ) : (
                  <span>
                    {suiteSummary.completed} {suiteSummary.completed === 1 ? 'request' : 'requests'} completed
                    {suiteSummary.timedOut > 0 ? ` · ${suiteSummary.timedOut} exhaustive ${suiteSummary.timedOut === 1 ? 'search' : 'searches'} reached the time budget` : ''}
                    {suiteSummary.failed > 0 ? ` · ${suiteSummary.failed} ${suiteSummary.failed === 1 ? 'request' : 'requests'} failed` : ''}
                    {suiteSummary.interrupted > 0 ? ` · ${suiteSummary.interrupted} request interrupted` : ''}
                    {` · ${suiteSummary.completed + suiteSummary.timedOut + suiteSummary.failed + suiteSummary.interrupted} of ${suiteSummary.total} attempted across ${suiteSummary.graphCount} ${suiteSummary.graphCount === 1 ? 'graph' : 'graphs'}.`}
                  </span>
                )}
                {suiteSummary.failures.length > 0 ? (
                  <details>
                    <summary>Review failed requests</summary>
                    <ul>
                      {suiteSummary.failures.map((failure) => (
                        <li key={`${failure.graphSlug}:${failure.operationTitle}`}>
                          {failure.graphLabel} · {failure.operationTitle}
                        </li>
                      ))}
                    </ul>
                  </details>
                ) : null}
              </div>
            ) : null}
            <div className="insights-lab-dialog__operations">
              {OPERATIONS.map((operation) => {
                const infoPanelId = `insights-lab-operation-${operation.id}-info`
                const isInfoExpanded = expandedOperationId === operation.id
                const missingTarget = operation.requiresTarget && !algorithmTargetNodeId
                const missingLeafTarget = operation.id === 'leaf' && !highestNode
                const wrongSource = operation.databaseOnly && graphDataSource !== 'database'
                const disabled = !graph
                  || isHistoryLoading
                  || isBusy
                  || !selectedBenchmarkSetId
                  || missingTarget
                  || missingLeafTarget
                  || wrongSource
                return (
                  <article className="insights-lab-dialog__operation" key={operation.id}>
                    <div>
                      <div className="insights-lab-dialog__operation-heading">
                        <h3>{operation.title}</h3>
                        <button
                          aria-controls={infoPanelId}
                          aria-expanded={isInfoExpanded}
                          aria-label={`About ${operation.title}`}
                          className="insights-lab-dialog__info-button"
                          disabled={isBusy}
                          onClick={() => setExpandedOperationId((current) => (
                            current === operation.id ? undefined : operation.id
                          ))}
                          type="button"
                        >
                          <span aria-hidden="true">i</span>
                        </button>
                      </div>
                      <p>{operation.description}</p>
                      <section
                        aria-label={`About ${operation.title}`}
                        className="insights-lab-dialog__algorithm-info"
                        hidden={!isInfoExpanded}
                        id={infoPanelId}
                      >
                        <p>{operation.explanation}</p>
                        <div>
                          <strong>Keep in mind</strong>
                          <p>{operation.caveat}</p>
                        </div>
                      </section>
                      {wrongSource ? <small>Database graphs only; fixture updates are skipped.</small> : null}
                      {missingTarget ? <small>This graph has no root node for this algorithm.</small> : null}
                      {missingLeafTarget ? <small>This graph has no node to update.</small> : null}
                    </div>
                    <button
                      aria-label={`Run ${operation.title}`}
                      disabled={disabled}
                      onClick={() => void launch(operation)}
                      type="button"
                    >
                      Run
                    </button>
                  </article>
                )
              })}
            </div>
          </section>
        ) : tab === 'history' ? (
          <section
            aria-labelledby="insights-lab-history-tab"
            className="insights-lab-dialog__panel insights-lab-dialog__history"
            id="insights-lab-history-panel"
            role="tabpanel"
          >
            {selectedRun ? (
              <div className="insights-lab-dialog__history-detail">
                <div className="insights-lab-dialog__history-detail-toolbar">
                  <button
                    onClick={() => {
                      focusRunListItemRef.current = selectedRun.runNumber
                      setSelectedRunNumber(undefined)
                    }}
                    ref={historyBackRef}
                    type="button"
                  >
                    <span aria-hidden="true">←</span> Back to all runs
                  </button>
                </div>
                <RunReport benchmarkSets={benchmarkSets} run={selectedRun} />
              </div>
            ) : (
              <>
                {isHistoryLoading ? <p role="status">Loading run history…</p> : null}
                {historyError ? <p className="insights-lab-dialog__error" role="alert">{historyError}</p> : null}
                {!isHistoryLoading && runs.length === 0 ? <p>No performance runs have been recorded yet.</p> : null}
                {runs.length > 0 ? (
                  <div
                    aria-label="Performance run history"
                    className="insights-lab-dialog__history-table-wrap"
                    role="region"
                    tabIndex={0}
                  >
                    <table className="insights-lab-dialog__history-table">
                      <thead><tr><th>Run</th><th>Date/time</th><th>Operation</th><th>Benchmark set</th><th>Graph</th><th>Operation time</th><th>Status</th></tr></thead>
                      <tbody>
                        {runs.map((run) => (
                          <tr key={run.runNumber}>
                            <td>
                              <button
                                aria-label={`View run ${run.runNumber}`}
                                onClick={() => {
                                  focusHistoryDetailRef.current = true
                                  setSelectedRunNumber(run.runNumber)
                                }}
                                ref={(button) => {
                                  if (button) runViewButtonRefs.current.set(run.runNumber, button)
                                  else runViewButtonRefs.current.delete(run.runNumber)
                                }}
                                type="button"
                              >
                                #{run.runNumber}
                              </button>
                            </td>
                            <td>{formatDateTime(run.startedAtUtc)}</td>
                            <td>{operationLabel(run)}</td>
                            <td>{benchmarkSetName(run.benchmarkSetId, benchmarkSets)}</td>
                            <td><code>{run.graph?.slug ?? '—'}</code></td>
                            <td>{formatMilliseconds(getOperationTime(run))}</td>
                            <td>{executionStatus(run)}{proofStatus(run) ? <small>{proofStatus(run)}</small> : null}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                ) : null}
              </>
            )}
          </section>
        ) : (
          <section
            aria-labelledby="insights-lab-trends-tab"
            className="insights-lab-dialog__panel"
            id="insights-lab-trends-panel"
            role="tabpanel"
          >
            <InsightsLabTrends benchmarkSets={benchmarkSets} runs={runs} />
          </section>
        )}
      </div>
    </div>
  )
}

function RunReport({
  benchmarkSets,
  run,
}: {
  benchmarkSets: readonly BenchmarkSet[]
  run: PerformanceRunRecord
}) {
  const setName = benchmarkSetName(run.benchmarkSetId, benchmarkSets)
  return (
    <article aria-label={`Report for run ${run.runNumber}`} className="insights-lab-dialog__report">
      <header>
        <div><span>Run #{run.runNumber}</span><h3>{operationLabel(run)}</h3></div>
        <div className="insights-lab-dialog__statuses">
          <span data-status={run.outcome?.status}>{executionStatus(run)}</span>
          {proofStatus(run) ? <span data-proof>{proofStatus(run)}</span> : null}
        </div>
      </header>
      <p>{formatDateTime(run.startedAtUtc)}</p>
      <p className="insights-lab-dialog__report-benchmark">
        <span>Benchmark set</span>
        <strong>{setName}</strong>
        {run.benchmarkSetId ? <code>{run.benchmarkSetId}</code> : null}
      </p>
      <TimeBudgetSummary run={run} />
      <ResultPreview run={run} />
      <ReportSection title="Algorithm" value={run.algorithm} />
      <ReportSection title="Graph" value={run.graph} />
      <ReportSection title="Invocation" value={run.invocation} />
      <ReportSection title="Timing" value={run.timing} />
      <ReportSection title="Resources" value={run.resources} />
      <ReportSection title="Outcome" value={presentedOutcome(run)} />
      <ReportSection title="Build" value={run.build} />
      <ReportSection title="Algorithm details" value={run.details} />
    </article>
  )
}

function numericDetail(
  details: Record<string, unknown>,
  name: string,
): number | undefined {
  const value = details[name]
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function displayDetail(value: unknown): string | undefined {
  if (typeof value === 'number' && Number.isFinite(value)) return value.toLocaleString()
  if (typeof value === 'string' && value.trim()) {
    if (/^\d+$/.test(value)) return BigInt(value).toLocaleString()
    return value
  }
  return undefined
}

function formatBudget(milliseconds?: number): string {
  if (milliseconds === undefined) return 'fixed compute-time'
  if (milliseconds >= 60_000 && milliseconds % 60_000 === 0) {
    const minutes = milliseconds / 60_000
    return `${minutes}-minute`
  }
  return `${formatMilliseconds(milliseconds)} compute-time`
}

function TimeBudgetSummary({ run }: { run: PerformanceRunRecord }) {
  const details = run.details ?? {}
  if (run.outcome?.status !== 'timedOut' && details.stopReason !== 'timeBudget') return null

  const parameters = run.invocation?.parameters
  const budget = parameters && typeof parameters === 'object'
    ? numericDetail(parameters, 'timeBudgetMilliseconds')
    : undefined
  const largestCompleted = numericDetail(details, 'largestCardinalityFullyExhausted')
  const activeCardinality = numericDetail(details, 'activeCardinality')
  const evaluatedAtActive = displayDetail(details.subsetEvaluationsAtActiveCardinality)
  const totalAtActive = displayDetail(details.totalSubsetsAtActiveCardinality)
  const subsetEvaluations = displayDetail(details.subsetEvaluations)
  const totalCandidates = displayDetail(details.totalCandidateCount)
  const timeoutStage = typeof details.timeoutStage === 'string'
    ? sentenceCase(details.timeoutStage)
    : undefined

  const frontierItems = [
    largestCompleted === undefined
      ? undefined
      : ['Fully exhausted', `All sets through size ${largestCompleted}`],
    activeCardinality === undefined
      ? undefined
      : ['Stopped while testing', `Sets of size ${activeCardinality}`],
    evaluatedAtActive === undefined
      ? undefined
      : ['At active size', totalAtActive ? `${evaluatedAtActive} of ${totalAtActive} sets evaluated` : `${evaluatedAtActive} sets evaluated`],
    subsetEvaluations === undefined
      ? undefined
      : ['Total subset evaluations', subsetEvaluations],
    totalCandidates === undefined
      ? undefined
      : ['Eligible counters', totalCandidates],
    timeoutStage === undefined
      ? undefined
      : ['Budget expired during', timeoutStage],
  ].filter((entry): entry is string[] => entry !== undefined)

  return (
    <section aria-label="Exhaustive search time limit" className="insights-lab-dialog__timeout-summary">
      <div>
        <strong>Search stopped at the {formatBudget(budget)} budget</strong>
        <p>
          This is an expected partial benchmark result, not a failed request. The search did not
          establish minimum-set proof before the server deadline, so its elapsed time is a lower
          bound on how long this exhaustive implementation would need to finish.
        </p>
      </div>
      {frontierItems.length > 0 ? (
        <dl>{frontierItems.map(([label, value]) => (
          <div key={label}><dt>{label}</dt><dd>{value}</dd></div>
        ))}</dl>
      ) : null}
    </section>
  )
}

function firstArray(details: Record<string, unknown>, names: readonly string[]): unknown[] | undefined {
  for (const name of names) if (Array.isArray(details[name])) return details[name] as unknown[]
  return undefined
}

function ResultPreview({ run }: { run: PerformanceRunRecord }) {
  const details = run.details ?? {}
  const sections: { title: string, items: unknown[], emptyLabel?: string }[] = []
  if (run.algorithm?.name === 'minimal-counter-set') {
    const items = firstArray(details, ['returnedNodeIds'])
    const stoppedDuringPreparation = (
      (run.outcome?.status === 'timedOut' || details.stopReason === 'timeBudget')
      && details.timeoutStage === 'preparation'
      && numericDetail(details, 'subsetEvaluations') === 0
    )
    if (items) sections.push({
      title: stoppedDuringPreparation
        ? 'Exhaustive search result'
        : details.thresholdReached === false
        ? run.outcome?.status === 'timedOut' || details.stopReason === 'timeBudget'
          ? 'Best set found before time limit — threshold not reached'
          : 'Best set examined — threshold not reached'
        : 'Returned node IDs',
      items,
      emptyLabel: stoppedDuringPreparation
        ? 'No candidate set evaluated before time limit'
        : details.thresholdReached === false
          ? 'No nodes (empty set)'
          : 'None (empty set)',
    })
  } else if (run.algorithm?.name === 'evidence-impact-ranking') {
    const supporting = firstArray(details, ['supportingPreview', 'topSupportingResults'])
    const counter = firstArray(details, ['counterPreview', 'topCounterResults'])
    if (supporting) sections.push({ title: 'Top supporting evidence', items: supporting })
    if (counter) sections.push({ title: 'Top counter evidence', items: counter })
  } else if (run.algorithm?.name === 'robustness-ranking') {
    const items = firstArray(details, ['rankingPreview', 'topResults'])
    if (items) sections.push({ title: 'Least robust nodes', items })
  } else if (run.algorithm?.name === 'least-robust-node') {
    const title = details.selectedNodeTitle
    if (typeof title === 'string') sections.push({ title: 'Least robust node', items: [title] })
  }
  if (sections.length === 0) return null
  return (
    <section className="insights-lab-dialog__preview">
      <h4>Result preview</h4>
      <div>{sections.map((section) => (
        <div key={section.title}>
          <strong>{section.title}</strong>
          {section.items.length === 0
            ? <span>{section.emptyLabel ?? 'None'}</span>
            : <ol>{section.items.map((item, index) => <li key={index}><StructuredValue value={item} /></li>)}</ol>}
        </div>
      ))}</div>
      {details.returnedNodeIdsTruncated === true ? <small>Preview truncated.</small> : null}
    </section>
  )
}

function ReportSection({ title, value }: { title: string, value: unknown }) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  const entries = Object.entries(value as Record<string, unknown>)
    .filter(([, entryValue]) => entryValue !== undefined)
  if (entries.length === 0) return null
  return (
    <section className="insights-lab-dialog__report-section">
      <h4>{title}</h4>
      <dl>{entries.map(([key, entryValue]) => (
        <div key={key}><dt>{sentenceCase(key)}</dt><dd><StructuredValue value={entryValue} /></dd></div>
      ))}</dl>
    </section>
  )
}

function StructuredValue({ value }: { value: unknown }): ReactNode {
  if (value === null || value === undefined || value === '') return <span>—</span>
  if (typeof value === 'boolean') return <span>{value ? 'Yes' : 'No'}</span>
  if (typeof value === 'number') return <span>{value.toLocaleString()}</span>
  if (typeof value === 'string') return <span className="insights-lab-dialog__value">{value}</span>
  if (Array.isArray(value)) {
    if (value.length === 0) return <span>None</span>
    return <ol className="insights-lab-dialog__structured-list">{value.map((item, index) => <li key={index}><StructuredValue value={item} /></li>)}</ol>
  }
  if (typeof value === 'object') {
    return <dl className="insights-lab-dialog__nested">{Object.entries(value as Record<string, unknown>).map(([key, nestedValue]) => (
      <div key={key}><dt>{sentenceCase(key)}</dt><dd><StructuredValue value={nestedValue} /></dd></div>
    ))}</dl>
  }
  return <span>{String(value)}</span>
}
