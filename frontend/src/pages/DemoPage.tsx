import { useState } from 'react'
import { GraphCanvas } from '../components/graph/GraphCanvas'
import { GraphDetailsPanel } from '../components/graph/GraphDetailsPanel'
import { GraphOverviewPanel } from '../components/graph/GraphOverviewPanel'
import { mapGraphToFlow } from '../components/graph/graphMapping'
import { sampleGraph } from '../fixtures/sampleGraph'
import './DemoPage.css'

const flowGraph = mapGraphToFlow(sampleGraph)

export function DemoPage() {
  const [selectedNodeId, setSelectedNodeId] = useState<string>()
  const selectedNode = sampleGraph.nodes.find((node) => node.id === selectedNodeId)

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
            <h2>{sampleGraph.title}</h2>
          </div>

          <GraphCanvas
            nodes={flowGraph.nodes}
            edges={flowGraph.edges}
            selectedNodeId={selectedNodeId}
            onNodeSelect={setSelectedNodeId}
          />

          <p>
            Click a node to inspect its details. Pan and zoom are handled by
            React Flow, with dagre providing the initial layout.
          </p>

        </article>

        <div className="demo-sidebar-stack">
          <GraphDetailsPanel node={selectedNode} />
          <GraphOverviewPanel
            title={sampleGraph.title}
            description={sampleGraph.description}
            nodeCount={sampleGraph.nodes.length}
            edgeCount={sampleGraph.edges.length}
            fixtureName={sampleGraph.slug}
          />
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
