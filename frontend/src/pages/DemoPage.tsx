import { useCallback, useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { GraphCanvas } from '../components/graph/GraphCanvas'
import { GraphDetailsPanel } from '../components/graph/GraphDetailsPanel'
import { GraphOverviewPanel } from '../components/graph/GraphOverviewPanel'
import { mapGraphToFlow } from '../components/graph/graphMapping'
import type { GraphFixture, GraphFixtureNode } from '../fixtures/sampleGraph'
import {
  addEdge,
  addNode,
  deleteNode,
  getDefaultGraphDataSource,
  getGraphBySlug,
  getNodeCounterSet,
  resetDatabase,
  updateEdge,
  updateNode,
  type GraphDataSource,
} from '../services/graphService'
import './DemoPage.css'

const DEMO_GRAPH_SLUG = 'sample-medium'
const FIXTURE_MUTATION_MESSAGE = 'This feature is not available in fixture mode.'
const DB_UNREACHABLE_TITLE = 'Unable to load graph'
const DB_UNREACHABLE_MESSAGE = 'Unable to load graph data right now.'

export function DemoPage() {
  const [graph, setGraph] = useState<GraphFixture | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [reloadKey, setReloadKey] = useState(0)
  const [selectedNodeId, setSelectedNodeId] = useState<string>()
  const [isGraphExpanded, setIsGraphExpanded] = useState(false)
  const [isResettingDatabase, setIsResettingDatabase] = useState(false)
  const [graphDataSource, setGraphDataSource] = useState<GraphDataSource>(() => getDefaultGraphDataSource())

  const dismissNodeDetails = useCallback(() => {
    setSelectedNodeId(undefined)
  }, [])

  const toggleGraphExpanded = useCallback(() => {
    setIsGraphExpanded((isExpanded) => !isExpanded)
  }, [])

  useEffect(() => {
    if (!isGraphExpanded) {
      return
    }

    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    return () => {
      document.body.style.overflow = previousOverflow
    }
  }, [isGraphExpanded])

  useEffect(() => {
    let isActive = true

    async function loadGraph() {
      setIsLoading(true)
      setError(null)

      try {
        //Grabs nodes from backend
        const result = await getGraphBySlug(DEMO_GRAPH_SLUG, graphDataSource)

        if (!isActive) {
          return
        }

        setGraph(result)
        setSelectedNodeId(undefined)
        // We remove the explicit reset here so that updates and additions
        // don't lose the user's current focus/selection during a reload.
        // Deletions still handle their own cleanup.

      } catch {
        if (!isActive) {
          return
        }

        setGraph(null)
        setError(DB_UNREACHABLE_MESSAGE)
      } finally {
        if (isActive) {
          setIsLoading(false)
        }
      }
    }

    void loadGraph()

    return () => {
      isActive = false
    }
  }, [graphDataSource, reloadKey])

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

    try {
      await deleteNode(DEMO_GRAPH_SLUG, nodeId)
      setReloadKey((prev) => prev + 1)
      setSelectedNodeId(undefined)
    } catch {
      setError('Failed to delete node from the server.')
    }
  }, [graph?.edges, graphDataSource])

  const handleUpdateNode = useCallback(async (nodeId: string, data: Partial<GraphFixtureNode>) => {
    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    try {
      await updateNode(DEMO_GRAPH_SLUG, nodeId, data)
      setReloadKey((prev) => prev + 1)
    } catch {
      setError('Failed to update node on the server.')
    }
  }, [graphDataSource])

  const handleNodeCounterSet = useCallback(async (nodeId: string) => {
    try {
      const counterNodeIds = await getNodeCounterSet(DEMO_GRAPH_SLUG, nodeId)
      console.log(counterNodeIds)
    } catch {
      setError('Failed to get the minimal counter set from the server.')
    }
  }, [])

  const handleAddSupportingNode = useCallback(async (
    parentId: string,
    data: Partial<GraphFixtureNode> = {},
    edge: { kind: 'support' | 'rebut', importanceToParent: number } = { kind: 'support', importanceToParent: 1 },
  ) => {
    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    const newNodeId = `node-${Date.now()}`
    const newNode: GraphFixtureNode = {
      ...data,
      id: newNodeId,
      kind: data.kind ?? 'claim',
      title: data.title ?? 'New Node',
      bodyText: data.bodyText ?? '',
    } as GraphFixtureNode

    try {
      await addNode(DEMO_GRAPH_SLUG, newNode, parentId, edge)
      setReloadKey((prev) => prev + 1)
      setSelectedNodeId(newNodeId)
    } catch {
      setError('Failed to add node to the server.')
    }
  }, [graphDataSource])

  const handleUpdateEdge = useCallback(async (
    edgeId: string,
    data: { importanceToParent?: number },
  ) => {
    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    try {
      await updateEdge(DEMO_GRAPH_SLUG, edgeId, data)
      setReloadKey((prev) => prev + 1)
    } catch {
      setError('Failed to update edge on the server.')
    }
  }, [graphDataSource])

  const handleAddParentEdge = useCallback(async (
    edge: { from: string, to: string, kind: 'support' | 'rebut', importanceToParent: number },
  ) => {
    if (graphDataSource === 'fixture') {
      alert(FIXTURE_MUTATION_MESSAGE)
      return
    }

    try {
      await addEdge(DEMO_GRAPH_SLUG, edge)
      setReloadKey((prev) => prev + 1)
    } catch {
      setError('Failed to add edge on the server.')
    }
  }, [graphDataSource])

  const handleResetDatabase = useCallback(async () => {
    if (!window.confirm('Are you sure you want to reset the database? This will restore the default seed data.')) {
      return
    }

    setIsResettingDatabase(true)
    setError(null)

    try {
      await resetDatabase()
      setSelectedNodeId(undefined)
      setReloadKey((prev) => prev + 1)
    } catch {
      setError('Failed to reset the database.')
    } finally {
      setIsResettingDatabase(false)
    }
  }, [])

  const handleGraphDataSourceChange = useCallback((nextDataSource: GraphDataSource) => {
    if (graphDataSource === nextDataSource) {
      return
    }

    setSelectedNodeId(undefined)
    setError(null)
    setGraphDataSource(nextDataSource)
  }, [graphDataSource])

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      // Prevent trigger if user is typing in an input or textarea
      if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
        return
      }

      if (event.key === 'Escape') {
        if (isGraphExpanded) {
          setIsGraphExpanded(false)
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

    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [selectedNodeId, isGraphExpanded, dismissNodeDetails, handleDeleteNode, handleAddSupportingNode, handleNodeCounterSet])

  const selectedNode = graph?.nodes.find((node) => node.id === selectedNodeId)
  const flowGraph = graph ? mapGraphToFlow(graph) : null
  const shouldShowOverviewPanel = graph !== null || error !== null
  const overviewTitle = graph?.title ?? DB_UNREACHABLE_TITLE
  const overviewDescription = graph?.description ?? error ?? ''
  const overviewNodeCount = graph?.nodes.length ?? 0
  const overviewEdgeCount = graph?.edges.length ?? 0
  const overviewFixtureName = graph?.slug ?? DB_UNREACHABLE_TITLE
  const detailsSheet = (
    <div
      className={`demo-details-sheet${isGraphExpanded ? ' demo-details-sheet--sheet-mode' : ''}${selectedNode ? ' demo-details-sheet--open' : ''}`}
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
          className={`demo-stage demo-stage--live${isGraphExpanded ? ' demo-stage--expanded' : ''}`}
          data-testid="demo-graph-stage"
        >
          <div className="demo-stage__header">
            <h2>{graph?.title ?? 'Loading graph demo'}</h2>
          </div>

          {isLoading ? (
            <div className="demo-state" data-testid="demo-loading-state">
              <h3>Loading graph…</h3>
              <p>Fetching the current reasoning graph and preparing the layout.</p>
            </div>
          ) : error ? (
            <div className="demo-state demo-state--error" data-testid="demo-error-state">
              <h3>{DB_UNREACHABLE_TITLE}</h3>
              <p>{error}</p>
              <button
                className="secondary-link demo-state__button"
                onClick={() => setReloadKey((value) => value + 1)}
                type="button"
              >
                Retry
              </button>
            </div>
          ) : flowGraph && graph ? (
            <GraphCanvas
              nodes={flowGraph.nodes}
              edges={flowGraph.edges}
              selectedNodeId={selectedNodeId}
              onNodeSelect={setSelectedNodeId}
              isExpanded={isGraphExpanded}
              onToggleExpanded={toggleGraphExpanded}
            />
          ) : null}

          <p>
            Click a node to inspect its details. Pan and zoom are handled by
            React Flow, with dagre providing the initial layout.
          </p>
        </article>

        <div className="demo-sidebar-stack">
          {isGraphExpanded ? null : detailsSheet}
          {shouldShowOverviewPanel ? (
            <GraphOverviewPanel
              title={overviewTitle}
              description={overviewDescription}
              nodeCount={overviewNodeCount}
              edgeCount={overviewEdgeCount}
              fixtureName={overviewFixtureName}
              dataSource={graphDataSource}
              isResettingDatabase={isResettingDatabase}
              onDataSourceChange={handleGraphDataSourceChange}
              onResetDatabase={handleResetDatabase}
            />
          ) : null}
        </div>
      </section>

      {isGraphExpanded ? createPortal(detailsSheet, document.body) : null}

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
