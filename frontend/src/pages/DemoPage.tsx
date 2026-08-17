import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { DatabaseResetDialog } from '../components/graph/DatabaseResetDialog'
import { InsightsGraphCanvas } from '../components/graph/InsightsGraphCanvas'
import { GraphDetailsPanel } from '../components/graph/GraphDetailsPanel'
import { GraphOverviewPanel } from '../components/graph/GraphOverviewPanel'
import type {
  GraphFixture,
  GraphFixtureEdge,
  GraphFixtureNode,
} from '../fixtures/sampleGraph'
import {
  addEdge,
  addNode,
  deleteNode,
  getDefaultGraphDataSource,
  getEvidenceImpactRanking,
  getGraphCatalog,
  getGraphBySlug,
  getLeastRobustNode,
  getNodeRobustnessRanking,
  getNodeCounterSet,
  resetDatabase,
  updateEdge,
  updateNode,
  type GraphDataSource,
} from '../services/graphService'
import type { GraphSummary } from '../services/graphTypes'
import { isStressGraphId, type StressGraphId } from '../services/stressGraphs'
import './DemoPage.css'

const FIXTURE_GRAPH_SLUG = 'sample-medium'
const FIXTURE_MUTATION_MESSAGE = 'This feature is not available in fixture mode.'
const DB_UNREACHABLE_TITLE = 'Unable to load graph'
const DB_UNREACHABLE_MESSAGE = 'Unable to load graph data right now.'
const DB_CATALOG_UNREACHABLE_TITLE = 'Unable to load graph catalog'
const DB_CATALOG_UNREACHABLE_MESSAGE = 'Unable to load the database graph list right now.'
const DB_EMPTY_TITLE = 'No database graphs'
const DB_EMPTY_MESSAGE = 'The database does not contain any graphs yet. Reset the database to restore the seed data.'
const DB_RESET_ERROR_MESSAGE = 'The database reset failed or could not be confirmed. The current view has been retained.'

type EdgeCreateFields = Pick<
  GraphFixtureEdge,
  | 'kind'
  | 'probabilityGivenParent'
  | 'probabilityGivenNotParent'
>

type EdgeUpdateFields = Partial<Pick<
  GraphFixtureEdge,
  | 'probabilityGivenParent'
  | 'probabilityGivenNotParent'
>>

export function DemoPage() {
  const graphCatalogRequestVersionRef = useRef(0)
  const graphRequestVersionRef = useRef(0)
  const [graph, setGraph] = useState<GraphFixture | null>(null)
  const [graphError, setGraphError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [reloadKey, setReloadKey] = useState(0)
  const [graphCatalog, setGraphCatalog] = useState<GraphSummary[]>([])
  const [graphCatalogError, setGraphCatalogError] = useState<string | null>(null)
  const [isGraphCatalogLoading, setIsGraphCatalogLoading] = useState(true)
  const [graphCatalogReloadKey, setGraphCatalogReloadKey] = useState(0)
  const [graphCatalogVersion, setGraphCatalogVersion] = useState(0)
  const [selectedDatabaseGraphSlug, setSelectedDatabaseGraphSlug] = useState<string>()
  const [selectedNodeId, setSelectedNodeId] = useState<string>()
  const [isGraphFullscreen, setIsGraphFullscreen] = useState(false)
  const [isResettingDatabase, setIsResettingDatabase] = useState(false)
  const [isResetDialogOpen, setIsResetDialogOpen] = useState(false)
  const [resetDatabaseError, setResetDatabaseError] = useState<string | null>(null)
  const [graphDataSource, setGraphDataSource] = useState<GraphDataSource>(() => getDefaultGraphDataSource())
  const installedStressGraphIds = useMemo(
    () => graphCatalog.map(({ slug }) => slug).filter(isStressGraphId),
    [graphCatalog],
  )
  const activeGraphSlug = graphDataSource === 'fixture'
    ? FIXTURE_GRAPH_SLUG
    : selectedDatabaseGraphSlug
  const activeCatalogVersion = graphDataSource === 'database' ? graphCatalogVersion : 0
  const activeGraphCatalogLoading = graphDataSource === 'database' && isGraphCatalogLoading
  const activeGraphCatalogError = graphDataSource === 'database' ? graphCatalogError : null

  const dismissNodeDetails = useCallback(() => {
    setSelectedNodeId(undefined)
  }, [])

  useEffect(() => {
    let isActive = true
    const requestVersion = ++graphCatalogRequestVersionRef.current

    async function loadGraphCatalog() {
      setIsGraphCatalogLoading(true)
      setGraphCatalogError(null)

      try {
        const summaries = await getGraphCatalog()

        if (!isActive || requestVersion !== graphCatalogRequestVersionRef.current) {
          return
        }

        setGraphCatalog(summaries)
        setSelectedDatabaseGraphSlug((currentSlug) => (
          summaries.some((summary) => summary.slug === currentSlug)
            ? currentSlug
            : summaries[0]?.slug
        ))
        setGraphCatalogVersion((version) => version + 1)
      } catch {
        if (isActive && requestVersion === graphCatalogRequestVersionRef.current) {
          setGraphCatalogError(DB_CATALOG_UNREACHABLE_MESSAGE)
        }
      } finally {
        if (isActive && requestVersion === graphCatalogRequestVersionRef.current) {
          setIsGraphCatalogLoading(false)
        }
      }
    }

    void loadGraphCatalog()

    return () => {
      isActive = false
    }
  }, [graphCatalogReloadKey])

  useEffect(() => {
    let isActive = true
    const requestVersion = ++graphRequestVersionRef.current

    if (activeGraphCatalogLoading || activeGraphCatalogError) {
      return () => {
        isActive = false
      }
    }

    if (!activeGraphSlug) {
      return () => {
        isActive = false
      }
    }
    const graphSlug = activeGraphSlug

    async function loadGraph() {
      setIsLoading(true)
      setGraphError(null)

      try {
        const result = await getGraphBySlug(graphSlug, graphDataSource)

        if (!isActive || requestVersion !== graphRequestVersionRef.current) {
          return
        }

        setGraph(result)
        // Preserve selection across mutation-driven reloads. Data-source
        // changes, resets, and deletions clear it in their own handlers.

      } catch {
        if (!isActive || requestVersion !== graphRequestVersionRef.current) {
          return
        }

        setGraph(null)
        setGraphError(DB_UNREACHABLE_MESSAGE)
      } finally {
        if (isActive && requestVersion === graphRequestVersionRef.current) {
          setIsLoading(false)
        }
      }
    }

    void loadGraph()

    return () => {
      isActive = false
    }
  }, [activeCatalogVersion, activeGraphCatalogError, activeGraphCatalogLoading, activeGraphSlug, graphDataSource, reloadKey])

  const handleDeleteNode = useCallback(async (nodeId: string) => {
    const hasInNeighbors = graph?.edges.some((e) => e.to === nodeId)
    if (hasInNeighbors) {
      alert('Cannot delete a node that has incoming neighbors. Remove the nodes pointing to this one first.')
      return
    }

    if (!window.confirm('Are you sure you want to delete this node? This action cannot be undone.')) {
      return
    }

    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    if (!activeGraphSlug) {
      return
    }

    try {
      await deleteNode(activeGraphSlug, nodeId)
      setReloadKey((prev) => prev + 1)
      setSelectedNodeId(undefined)
    } catch {
      setGraphError('Failed to delete node from the server.')
    }
  }, [activeGraphSlug, graph?.edges, graphDataSource])

  const handleUpdateNode = useCallback(async (nodeId: string, data: Partial<GraphFixtureNode>) => {
    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    if (!activeGraphSlug) {
      return
    }

    try {
      await updateNode(activeGraphSlug, nodeId, data)
      setReloadKey((prev) => prev + 1)
    } catch {
      setGraphError('Failed to update node on the server.')
    }
  }, [activeGraphSlug, graphDataSource])

  const handleNodeCounterSet = useCallback(async (nodeId: string) => {
    if (!activeGraphSlug) {
      return
    }

    try {
      const counterNodeIds = await getNodeCounterSet(activeGraphSlug, nodeId, graphDataSource)
      console.log(counterNodeIds)
    } catch {
      setGraphError('Failed to get the minimal counter set from the server.')
    }
  }, [activeGraphSlug, graphDataSource])

  const handleEvidenceImpactRanking = useCallback(async (nodeId: string) => {
    if (!activeGraphSlug) {
      return
    }

    try {
      const ranking = await getEvidenceImpactRanking(activeGraphSlug, nodeId, graphDataSource)
      const isEvidenceNode = (nodeId: string) => graph?.nodes.some((node) =>
        node.id === nodeId && (node.kind === 'evidence' || node.kind === 'objection')
      )

      console.log({
        supportingEvidence: ranking.supportingEvidence.filter((impact) => isEvidenceNode(impact.nodeId)),
        counterEvidence: ranking.counterEvidence.filter((impact) => isEvidenceNode(impact.nodeId)),
      })
    } catch {
      setGraphError('Failed to get the evidence impact ranking from the server.')
    }
  }, [activeGraphSlug, graph?.nodes, graphDataSource])

  const handleLeastRobustNode = useCallback(async () => {
    if (!activeGraphSlug) {
      return
    }

    try {
      const result = await getLeastRobustNode(activeGraphSlug, graphDataSource)
      console.log(`${result.nodeTitle}: ${result.robustness}`)
    } catch {
      setGraphError('Failed to get the least robust node from the server.')
    }
  }, [activeGraphSlug, graphDataSource])

  const handleNodeRobustnessRanking = useCallback(async () => {
    if (!activeGraphSlug) {
      return
    }

    try {
      const ranking = await getNodeRobustnessRanking(activeGraphSlug, graphDataSource)
      console.log(ranking.map(({ nodeId, robustness }) => ({ nodeId, robustness })))
    } catch {
      setGraphError('Failed to get the node robustness ranking from the server.')
    }
  }, [activeGraphSlug, graphDataSource])

  const handleAddSupportingNode = useCallback(async (
    parentId: string,
    data: Partial<GraphFixtureNode> = {},
    edge: EdgeCreateFields = {
      kind: 'support',
      probabilityGivenParent: 0.5,
      probabilityGivenNotParent: 0.5,
    },
  ) => {
    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    const newNodeId = `node-${Date.now()}`
    const priorOdds = data.priorOdds ?? 0
    const newNode: GraphFixtureNode = {
      ...data,
      id: newNodeId,
      kind: data.kind ?? 'claim',
      title: data.title ?? 'New Node',
      bodyText: data.bodyText ?? '',
      priorOdds,
      posteriorOdds: data.posteriorOdds ?? priorOdds,
    }

    if (!activeGraphSlug) {
      return
    }

    try {
      await addNode(activeGraphSlug, newNode, parentId, edge)
      setReloadKey((prev) => prev + 1)
      setSelectedNodeId(newNodeId)
    } catch {
      setGraphError('Failed to add node to the server.')
    }
  }, [activeGraphSlug, graphDataSource])

  const handleUpdateEdge = useCallback(async (
    edgeId: string,
    data: EdgeUpdateFields,
  ) => {
    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    if (!activeGraphSlug) {
      return
    }

    try {
      await updateEdge(activeGraphSlug, edgeId, data)
      setReloadKey((prev) => prev + 1)
    } catch {
      setGraphError('Failed to update edge on the server.')
    }
  }, [activeGraphSlug, graphDataSource])

  const handleAddParentEdge = useCallback(async (
    edge: Omit<GraphFixtureEdge, 'id'>,
  ) => {
    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    if (!activeGraphSlug) {
      return
    }

    try {
      await addEdge(activeGraphSlug, edge)
      setReloadKey((prev) => prev + 1)
    } catch {
      setGraphError('Failed to add edge on the server.')
    }
  }, [activeGraphSlug, graphDataSource])

  const handleResetDatabase = useCallback(async (stressGraphIds: StressGraphId[]) => {
    setIsResettingDatabase(true)
    setResetDatabaseError(null)

    try {
      await resetDatabase(stressGraphIds)
      graphCatalogRequestVersionRef.current += 1
      graphRequestVersionRef.current += 1
      setSelectedDatabaseGraphSlug(undefined)
      setSelectedNodeId(undefined)
      setGraph(null)
      setGraphCatalog([])
      setGraphCatalogError(null)
      setGraphError(null)
      setIsLoading(true)
      setIsGraphCatalogLoading(true)
      setIsResetDialogOpen(false)
      setGraphCatalogReloadKey((key) => key + 1)
    } catch {
      setResetDatabaseError(DB_RESET_ERROR_MESSAGE)
    } finally {
      setIsResettingDatabase(false)
    }
  }, [])

  const handleOpenResetDialog = useCallback(() => {
    setResetDatabaseError(null)
    setIsResetDialogOpen(true)
  }, [])

  const handleCloseResetDialog = useCallback(() => {
    if (!isResettingDatabase) {
      setResetDatabaseError(null)
      setIsResetDialogOpen(false)
    }
  }, [isResettingDatabase])

  const handleGraphDataSourceChange = useCallback((nextDataSource: GraphDataSource) => {
    if (graphDataSource === nextDataSource) {
      return
    }

    setSelectedNodeId(undefined)
    setGraphError(null)
    setGraph(null)
    setIsLoading(true)
    setGraphDataSource(nextDataSource)
  }, [graphDataSource])

  const handleGraphChange = useCallback((slug: string) => {
    if (slug === selectedDatabaseGraphSlug) {
      return
    }

    setSelectedNodeId(undefined)
    setGraphError(null)
    setGraph(null)
    setIsLoading(true)
    setSelectedDatabaseGraphSlug(slug)
  }, [selectedDatabaseGraphSlug])

  const handleGraphNodeSelect = useCallback((node: GraphFixtureNode | null) => {
    setSelectedNodeId(node?.id)
  }, [])

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented || isResetDialogOpen) {
        return
      }

      // Prevent trigger if user is typing in an input or textarea
      if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
        return
      }

      if (event.key === 'Escape') {
        if (isGraphFullscreen) {
          setIsGraphFullscreen(false)
        } else {
          dismissNodeDetails()
        }
      }
      else if (event.key.toLowerCase() === 'd') {
        if (selectedNodeId !== undefined) {
          void handleDeleteNode(selectedNodeId)
        }
      }
      else if (event.key.toLowerCase() === 'a') {
        if (selectedNodeId !== undefined) {
          void handleAddSupportingNode(selectedNodeId)
        }
      }
      else if (event.key.toLowerCase() === 'i') {
        if (selectedNodeId !== undefined) {
          void handleNodeCounterSet(selectedNodeId)
        }
      }
      else if (event.key.toLowerCase() === 'e') {
        if (selectedNodeId !== undefined) {
          void handleEvidenceImpactRanking(selectedNodeId)
        }
      }
      else if (event.key.toLowerCase() === 'r') {
        void handleLeastRobustNode()
      }
      else if (event.key.toLowerCase() === 'j') {
        void handleNodeRobustnessRanking()
      }

    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [selectedNodeId, isGraphFullscreen, isResetDialogOpen, dismissNodeDetails, handleDeleteNode, handleAddSupportingNode, handleNodeCounterSet, handleEvidenceImpactRanking, handleLeastRobustNode, handleNodeRobustnessRanking])

  const selectedNode = graph?.nodes.find((node) => node.id === selectedNodeId)
  const activeGraphSummary = graphDataSource === 'database'
    ? graphCatalog.find((summary) => summary.slug === selectedDatabaseGraphSlug)
    : undefined
  const isDatabaseCatalogEmpty = graphDataSource === 'database'
    && !isGraphCatalogLoading
    && graphCatalogError === null
    && graphCatalogVersion > 0
    && graphCatalog.length === 0
  const currentError = graphDataSource === 'database'
    ? graphCatalogError ?? graphError
    : graphError
  const currentErrorTitle = graphDataSource === 'database' && graphCatalogError
    ? DB_CATALOG_UNREACHABLE_TITLE
    : DB_UNREACHABLE_TITLE
  const isPageLoading = currentError === null
    && !isDatabaseCatalogEmpty
    && (isLoading || (graphDataSource === 'database' && isGraphCatalogLoading))
  const shouldShowOverviewPanel = graph !== null
    || currentError !== null
    || isDatabaseCatalogEmpty
    || (graphDataSource === 'database' && graphCatalog.length > 0)
  const overviewTitle = graph?.title
    ?? (currentError ? currentErrorTitle : undefined)
    ?? (isDatabaseCatalogEmpty ? DB_EMPTY_TITLE : undefined)
    ?? activeGraphSummary?.title
    ?? 'Loading graph'
  const overviewDescription = graph?.description
    ?? currentError
    ?? (isDatabaseCatalogEmpty ? DB_EMPTY_MESSAGE : undefined)
    ?? activeGraphSummary?.description
    ?? ''
  const overviewNodeCount = graph?.nodes.length ?? 0
  const overviewEdgeCount = graph?.edges.length ?? 0
  const overviewFixtureName = graph?.slug ?? activeGraphSlug ?? overviewTitle
  const stageTitle = graph?.title
    ?? (currentError ? currentErrorTitle : undefined)
    ?? (isDatabaseCatalogEmpty ? DB_EMPTY_TITLE : undefined)
    ?? activeGraphSummary?.title
    ?? 'Loading graph demo'
  const detailsSheet = (
    <div
      className={`demo-details-sheet${isGraphFullscreen ? ' demo-details-sheet--sheet-mode' : ''}${selectedNode ? ' demo-details-sheet--open' : ''}`}
      data-testid="demo-details-sheet"
    >
      <button
        aria-label="Dismiss node details"
        className="demo-details-sheet__backdrop"
        onClick={dismissNodeDetails}
        tabIndex={-1}
        type="button"
      />
      <div
        aria-label="Node details"
        className="demo-details-sheet__surface"
        role="region"
      >
        <div className="demo-details-sheet__mobile-header">
          <span className="demo-details-sheet__handle" aria-hidden="true" />
          <button
            aria-label="Close node details"
            className="demo-details-sheet__close"
            onClick={dismissNodeDetails}
            type="button"
          >
            Close
          </button>
        </div>
        <GraphDetailsPanel
          node={selectedNode}
          nodes={graph?.nodes}
          edges={graph?.edges}
          onDelete={handleDeleteNode}
          onAddSupporting={handleAddSupportingNode}
          onUpdate={handleUpdateNode}
          onUpdateEdge={handleUpdateEdge}
          onAddParentEdge={handleAddParentEdge}
        />
      </div>
    </div>
  )

  return (
    <div className="page-shell demo-page-shell" data-testid="demo-page">
      <section className="demo-page-intro">
        <div className="demo-page-intro__copy">
          <span className="eyebrow demo-page-intro__eyebrow">Interactive Graph Demo</span>
        </div>
      </section>

      <section className="demo-visualization-grid">
        <article
          className="demo-stage demo-stage--live"
          data-testid="demo-graph-stage"
        >
          <div className="demo-stage__header">
            <h2>{stageTitle}</h2>
          </div>

          {isPageLoading ? (
            <div className="demo-state" data-testid="demo-loading-state">
              <h3>Loading graph…</h3>
              <p>Fetching the current reasoning graph and preparing the layout.</p>
            </div>
          ) : currentError ? (
            <div className="demo-state demo-state--error" data-testid="demo-error-state">
              <h3>{currentErrorTitle}</h3>
              <p>{currentError}</p>
              <button
                className="secondary-link demo-state__button"
                onClick={() => {
                  if (graphDataSource === 'database' && graphCatalogError) {
                    setGraphCatalogReloadKey((key) => key + 1)
                  } else {
                    setReloadKey((value) => value + 1)
                  }
                }}
                type="button"
              >
                Retry
              </button>
            </div>
          ) : isDatabaseCatalogEmpty ? (
            <div className="demo-state" data-testid="demo-empty-state">
              <h3>{DB_EMPTY_TITLE}</h3>
              <p>{DB_EMPTY_MESSAGE}</p>
            </div>
          ) : graph ? (
            <InsightsGraphCanvas
              graph={graph}
              selectedNodeId={selectedNodeId}
              onNodeSelect={handleGraphNodeSelect}
              isFullscreen={isGraphFullscreen}
              onFullscreenChange={setIsGraphFullscreen}
            />
          ) : null}

          <p>
            Click a node to inspect its details. Expand branches progressively
            to explore the graph while keeping the current reasoning path compact.
          </p>
        </article>

        <div className="demo-sidebar-stack">
          {isGraphFullscreen ? null : detailsSheet}
          {shouldShowOverviewPanel ? (
            <GraphOverviewPanel
              title={overviewTitle}
              description={overviewDescription}
              nodeCount={overviewNodeCount}
              edgeCount={overviewEdgeCount}
              fixtureName={overviewFixtureName}
              dataSource={graphDataSource}
              graphs={graphCatalog}
              selectedGraphSlug={selectedDatabaseGraphSlug}
              isGraphCatalogLoading={isGraphCatalogLoading}
              isResettingDatabase={isResettingDatabase}
              onDataSourceChange={handleGraphDataSourceChange}
              onGraphChange={handleGraphChange}
              onResetDatabase={handleOpenResetDialog}
            />
          ) : null}
        </div>
      </section>

      {isGraphFullscreen ? createPortal(detailsSheet, document.body) : null}

      <DatabaseResetDialog
        error={resetDatabaseError}
        initialSelectedStressGraphIds={installedStressGraphIds}
        isOpen={isResetDialogOpen}
        isSubmitting={isResettingDatabase}
        onCancel={handleCloseResetDialog}
        onConfirm={handleResetDatabase}
      />

      <section className="demo-support-strip">
        <article className="feature-card">
          <h3>Why this phase matters</h3>
          <p>
            The demo page is now graph-first. Later API work can replace the
            local fixture without changing the overall page composition.
          </p>
        </article>

        <article className="feature-card">
          <h3>Deliberately simple nodes</h3>
          <p>
            Nodes stay readable in the canvas while richer context moves into the
            details panel where it belongs.
          </p>
        </article>
      </section>
    </div>
  )
}
