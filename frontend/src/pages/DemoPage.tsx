import { useEffect, useState } from 'react'
import { GraphCanvas } from '../components/graph/GraphCanvas'
import { GraphDetailsPanel } from '../components/graph/GraphDetailsPanel'
import { GraphOverviewPanel } from '../components/graph/GraphOverviewPanel'
import { mapGraphToFlow } from '../components/graph/graphMapping'
import type { GraphFixture } from '../fixtures/sampleGraph'
import { getGraphBySlug } from '../services/graphService'
import './DemoPage.css'

const DEMO_GRAPH_SLUG = 'sample-medium'

export function DemoPage() {
  const [graph, setGraph] = useState<GraphFixture | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [reloadKey, setReloadKey] = useState(0)
  const [selectedNodeId, setSelectedNodeId] = useState<string>()

  useEffect(() => {
    let isActive = true

    async function loadGraph() {
      setIsLoading(true)
      setError(null)

      try {
        const result = await getGraphBySlug(DEMO_GRAPH_SLUG)

        if (!isActive) {
          return
        }

        setGraph(result)
        setSelectedNodeId(undefined)
      } catch {
        if (!isActive) {
          return
        }

        setGraph(null)
        setError('Unable to load graph data right now.')
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
  }, [reloadKey])

  const selectedNode = graph?.nodes.find((node) => node.id === selectedNodeId)
  const flowGraph = graph ? mapGraphToFlow(graph) : null

  return (
    <div className="page-shell demo-page-shell" data-testid="demo-page">
      <section className="demo-page-intro">
        <div className="demo-page-intro__copy">
          <span className="eyebrow demo-page-intro__eyebrow">Interactive Graph Demo</span>
        </div>
      </section>

      <section className="demo-visualization-grid">
        <article className="demo-stage demo-stage--live">
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
              <h3>Unable to load graph</h3>
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
            />
          ) : null}

          <p>
            Click a node to inspect its details. Pan and zoom are handled by
            React Flow, with dagre providing the initial layout.
          </p>
        </article>

        <div className="demo-sidebar-stack">
          <GraphDetailsPanel node={selectedNode} />
          {graph ? (
            <GraphOverviewPanel
              title={graph.title}
              description={graph.description}
              nodeCount={graph.nodes.length}
              edgeCount={graph.edges.length}
              fixtureName={graph.slug}
            />
          ) : null}
        </div>
      </section>

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
