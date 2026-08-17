import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import type { GraphFixture, GraphFixtureNode } from '../../fixtures/sampleGraph'
import {
  getBoundedNodeCounterSet,
  getEvidenceImpactRanking,
  getLeastRobustNode,
  getNodeCounterSet,
  getNodeRobustnessRanking,
  updateNode,
  type GraphDataSource,
} from '../../services/graphService'
import {
  getPerformanceRuns,
  type PerformanceReportDocument,
  type PerformanceRunRecord,
} from '../../services/performanceRuns'
import './InsightsLabDialog.css'

export interface InsightsLabDialogProps {
  isOpen: boolean
  graph: GraphFixture | null
  graphDataSource: GraphDataSource
  selectedNodeId?: string
  onClose: () => void
  onGraphUpdated?: () => void
}

type LabTab = 'run' | 'history'
type OperationId = 'minimal' | 'bounded' | 'evidence' | 'least' | 'ranking' | 'leaf'

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

const OPERATIONS: readonly OperationDefinition[] = [
  {
    id: 'minimal',
    title: 'Minimal counter set',
    description: 'Run the greedy counter-set search for the selected node.',
    explanation: 'Quickly looks for a small group of existing counterarguments that would lower the selected node below the Lab\'s confidence cutoff. It considers the most promising counters first.',
    caveat: 'This is a fast search, not a proof: a smaller counter set may exist.',
    algorithmName: 'minimal-counter-set',
    implementation: 'greedy',
    requiresTarget: true,
    cancellable: true,
  },
  {
    id: 'bounded',
    title: 'Bounded minimal counter set',
    description: 'Run the exact bounded search for the selected node.',
    explanation: 'Tries counterargument combinations from smallest to largest to find the fewest that would lower the selected node below the confidence cutoff.',
    caveat: 'The search considers at most 20 candidates. If more eligible counters exist, the result may be useful without being proven globally minimal.',
    algorithmName: 'minimal-counter-set',
    implementation: 'bounded-brute-force',
    requiresTarget: true,
    cancellable: true,
  },
  {
    id: 'evidence',
    title: 'Evidence impact ranking',
    description: 'Rank evidence by its impact on the selected node.',
    explanation: 'Estimates how much each supporting or opposing piece of evidence affects the selected node by calculating what its confidence would be without that evidence. It separates supporting and counter evidence, then ranks each group by the predicted change.',
    caveat: 'This measures sensitivity within the model, not whether a piece of evidence is true or caused the outcome.',
    algorithmName: 'evidence-impact-ranking',
    requiresTarget: true,
  },
  {
    id: 'least',
    title: 'Least robust node',
    description: 'Find the least robust node in the active graph.',
    explanation: 'Checks every node to estimate how much its confidence would change if the modeled contribution from its highest-weighted path disappeared, then returns the node with the largest change.',
    caveat: 'A lower robustness score means the node is easier to disrupt under this specific path-removal test.',
    algorithmName: 'least-robust-node',
    cancellable: true,
  },
  {
    id: 'ranking',
    title: 'Robustness ranking',
    description: 'Rank every node in the active graph by robustness.',
    explanation: 'Applies the same highest-weighted-path test to every node and orders the results from least to most robust.',
    caveat: 'This measures sensitivity to removing one modeled path, not the overall quality or truth of a node.',
    algorithmName: 'robustness-ranking',
    cancellable: true,
  },
  {
    id: 'leaf',
    title: 'Leaf update',
    description: 'Reapply prior odds to the ordinal-highest node and measure the update path.',
    explanation: 'Exercises the real database update and recalculation path without intentionally changing the graph. It writes the highest-ID node\'s existing prior odds back to that node, recalculates affected ancestors, and saves the results.',
    caveat: 'The highest-ID node is treated as a likely leaf but is not guaranteed to be one. This operation is unavailable for fixture graphs.',
    algorithmName: 'leaf-update',
    databaseOnly: true,
  },
]

const FOCUSABLE_SELECTOR = [
  'button:not(:disabled)',
  '[href]',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

const CANCEL_REPORT_POLL_ATTEMPTS = 8
const CANCEL_REPORT_POLL_INTERVAL_MS = 250

function sortRuns(document: PerformanceReportDocument): PerformanceRunRecord[] {
  return [...(Array.isArray(document.runs) ? document.runs : [])]
    .sort((left, right) => right.runNumber - left.runNumber)
}

function getHighestOrdinalNode(nodes: readonly GraphFixtureNode[]): GraphFixtureNode | undefined {
  return nodes.reduce<GraphFixtureNode | undefined>((highest, node) => (
    highest === undefined || node.id > highest.id ? node : highest
  ), undefined)
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
    return run.algorithm?.implementation === 'bounded-brute-force'
      ? 'Bounded minimal counter set'
      : 'Minimal counter set'
  }
  return ({
    'evidence-impact-ranking': 'Evidence impact ranking',
    'least-robust-node': 'Least robust node',
    'robustness-ranking': 'Robustness ranking',
    'leaf-update': 'Leaf update',
  } as Record<string, string>)[name ?? ''] ?? sentenceCase(name)
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
  return run.outcome?.status === 'notProven'
    ? 'Completed'
    : sentenceCase(run.outcome?.status)
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
  graph: GraphFixture,
  graphDataSource: GraphDataSource,
  targetNodeId?: string,
): boolean {
  if (run.algorithm?.name !== operation.algorithmName || run.graph?.slug !== graph.slug) return false
  if (run.invocation?.dataSource !== graphDataSource) return false
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
  selectedNodeId,
  onClose,
  onGraphUpdated,
}: OpenDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef<HTMLButtonElement>(null)
  const runTabRef = useRef<HTMLButtonElement>(null)
  const historyTabRef = useRef<HTMLButtonElement>(null)
  const historyBackRef = useRef<HTMLButtonElement>(null)
  const runViewButtonRefs = useRef(new Map<number, HTMLButtonElement>())
  const runningStatusRef = useRef<HTMLDivElement>(null)
  const focusHistoryDetailRef = useRef(false)
  const focusRunListItemRef = useRef<number | undefined>(undefined)
  const abortControllerRef = useRef<AbortController | null>(null)
  const runGuardRef = useRef(false)
  const [tab, setTab] = useState<LabTab>('run')
  const [expandedOperationId, setExpandedOperationId] = useState<OperationId>()
  const [runs, setRuns] = useState<PerformanceRunRecord[]>([])
  const [selectedRunNumber, setSelectedRunNumber] = useState<number>()
  const [isHistoryLoading, setIsHistoryLoading] = useState(true)
  const [historyError, setHistoryError] = useState<string | null>(null)
  const [runError, setRunError] = useState<string | null>(null)
  const [runNotice, setRunNotice] = useState<string | null>(null)
  const [activeOperation, setActiveOperation] = useState<OperationDefinition | null>(null)
  const [isCancellationRequested, setIsCancellationRequested] = useState(false)
  const [isFinalizingRun, setIsFinalizingRun] = useState(false)

  const selectedNode = useMemo(
    () => graph?.nodes.find(({ id }) => id === selectedNodeId),
    [graph, selectedNodeId],
  )
  const highestNode = useMemo(
    () => graph ? getHighestOrdinalNode(graph.nodes) : undefined,
    [graph],
  )
  const selectedRun = runs.find(({ runNumber }) => runNumber === selectedRunNumber)

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
      for (const { element, hadInert, previousAriaHidden } of isolatedElements) {
        if (!hadInert) element.removeAttribute('inert')
        if (previousAriaHidden === null) element.removeAttribute('aria-hidden')
        else element.setAttribute('aria-hidden', previousAriaHidden)
      }
      previousFocus?.focus()
    }
  }, [])

  useEffect(() => {
    if (activeOperation) runningStatusRef.current?.focus()
  }, [activeOperation, isCancellationRequested, isFinalizingRun])

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
    let nextTab: LabTab | undefined
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      nextTab = tab === 'run' ? 'history' : 'run'
    } else if (event.key === 'Home') {
      nextTab = 'run'
    } else if (event.key === 'End') {
      nextTab = 'history'
    }
    if (!nextTab) return
    event.preventDefault()
    setTab(nextTab)
    if (nextTab === 'history') setExpandedOperationId(undefined)
    const nextTabRef = nextTab === 'run' ? runTabRef : historyTabRef
    nextTabRef.current?.focus()
  }

  const launch = async (operation: OperationDefinition) => {
    if (runGuardRef.current || !graph) return
    const targetNode = operation.id === 'leaf' ? highestNode : selectedNode
    if (operation.requiresTarget && !targetNode) return
    if (operation.id === 'leaf' && !targetNode) return
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
    let launchFailure: unknown
    let requestStarted = false
    let requestCompleted = false

    try {
      const before = sortRuns(await getPerformanceRuns())
      setRuns(before)
      watermark = before.reduce((maximum, run) => Math.max(maximum, run.runNumber), -1)
      if (controller?.signal.aborted) throw new DOMException('Aborted', 'AbortError')

      try {
        requestStarted = true
        switch (operation.id) {
          case 'minimal':
            await getNodeCounterSet(graph.slug, targetNode!.id, graphDataSource, controller?.signal)
            break
          case 'bounded': {
            const result = await getBoundedNodeCounterSet(
              graph.slug,
              targetNode!.id,
              graphDataSource,
              controller?.signal,
            )
            exactRunNumber = result.runNumber
            break
          }
          case 'evidence':
            await getEvidenceImpactRanking(graph.slug, targetNode!.id, graphDataSource)
            break
          case 'least':
            await getLeastRobustNode(graph.slug, graphDataSource, controller?.signal)
            break
          case 'ranking':
            await getNodeRobustnessRanking(graph.slug, graphDataSource, controller?.signal)
            break
          case 'leaf':
            await updateNode(graph.slug, targetNode!.id, { priorOdds: targetNode!.priorOdds })
            onGraphUpdated?.()
            break
        }
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
        after = sortRuns(await getPerformanceRuns())
        setRuns(after)
        setHistoryError(null)
        matchingRun = exactRunNumber === undefined
          ? after.find((run) => (
            run.runNumber > watermark
            && runMatches(run, operation, graph, graphDataSource, targetNode?.id)
          ))
          : after.find(({ runNumber }) => runNumber === exactRunNumber)
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
          <div>
            <span className="eyebrow">Performance workspace</span>
            <h2 id="insights-lab-title">Insights Lab</h2>
            <p>{graph ? `${graph.title} · ${graph.slug}` : 'No active graph'}</p>
          </div>
          <button
            aria-label="Close Insights Lab"
            className="insights-lab-dialog__close"
            disabled={activeOperation !== null}
            onClick={onClose}
            ref={closeRef}
            type="button"
          >
            Close
          </button>
        </header>

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
        </div>

        {activeOperation ? (
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
                {isFinalizingRun ? 'Finalizing…' : isCancellationRequested ? 'Cancelling…' : 'Running…'}
              </strong>
              <span>{isFinalizingRun ? 'Waiting for the stored report.' : activeOperation.title}</span>
            </div>
            {activeOperation.cancellable && !isFinalizingRun ? (
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
            ) : !isFinalizingRun ? <span>This run cannot be cancelled.</span> : null}
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
            <div className="insights-lab-dialog__context">
              <span>Selected node</span>
              <strong>
                {selectedNode
                  ? `${selectedNode.title} (${selectedNode.id})`
                  : 'Select a node for counter-set and evidence-impact operations'}
              </strong>
            </div>
            <div className="insights-lab-dialog__operations">
              {OPERATIONS.map((operation) => {
                const infoPanelId = `insights-lab-operation-${operation.id}-info`
                const isInfoExpanded = expandedOperationId === operation.id
                const missingTarget = operation.requiresTarget && !selectedNode
                const missingLeafTarget = operation.id === 'leaf' && !highestNode
                const wrongSource = operation.databaseOnly && graphDataSource !== 'database'
                const disabled = !graph
                  || isHistoryLoading
                  || activeOperation !== null
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
                          disabled={activeOperation !== null}
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
                      {missingTarget ? <small>Select a node to enable this algorithm.</small> : null}
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
        ) : (
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
                <RunReport run={selectedRun} />
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
                      <thead><tr><th>Run</th><th>Date/time</th><th>Operation</th><th>Graph</th><th>Operation time</th><th>Status</th></tr></thead>
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
        )}
      </div>
    </div>
  )
}

function RunReport({ run }: { run: PerformanceRunRecord }) {
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

function firstArray(details: Record<string, unknown>, names: readonly string[]): unknown[] | undefined {
  for (const name of names) if (Array.isArray(details[name])) return details[name] as unknown[]
  return undefined
}

function ResultPreview({ run }: { run: PerformanceRunRecord }) {
  const details = run.details ?? {}
  const sections: { title: string, items: unknown[] }[] = []
  if (run.algorithm?.name === 'minimal-counter-set') {
    const items = firstArray(details, ['returnedNodeIds'])
    if (items) sections.push({ title: 'Returned node IDs', items })
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
        <div key={section.title}><strong>{section.title}</strong><ol>{section.items.map((item, index) => <li key={index}><StructuredValue value={item} /></li>)}</ol></div>
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
