import { useState } from 'react'
import { GraphCanvas } from '../components/graph/GraphCanvas'
import { GraphDetailsPanel } from '../components/graph/GraphDetailsPanel'
import { mapGraphToFlow } from '../components/graph/graphMapping'
import { sampleGraph } from '../fixtures/sampleGraph'

const flowGraph = mapGraphToFlow(sampleGraph)

export function DemoPage() {
  const [selectedNodeId, setSelectedNodeId] = useState<string>()
  const selectedNode = sampleGraph.nodes.find((node) => node.id === selectedNodeId)

  return (
    <div className="page-shell demo-page-shell" data-testid="demo-page">
      <section className="demo-page-intro">
        <div className="demo-page-intro__copy">
          <span className="eyebrow">Interactive Graph Demo</span>
          <h1>{sampleGraph.title}</h1>
          <p>{sampleGraph.description}</p>
        </div>

        <div className="demo-page-intro__stats" aria-label="Graph overview">
          <div className="demo-stat">
            <strong>{sampleGraph.nodes.length}</strong>
            <span>Nodes</span>
          </div>
          <div className="demo-stat">
            <strong>{sampleGraph.edges.length}</strong>
            <span>Edges</span>
          </div>
          <div className="demo-stat">
            <strong>{sampleGraph.slug}</strong>
            <span>Fixture</span>
          </div>
        </div>
      </section>

      <section className="demo-visualization-grid">
        <article className="demo-stage demo-stage--live">
          <div className="demo-stage__header">
            <div>
              <span className="eyebrow">Graph Canvas</span>
              <h2>Explore the local reasoning graph fixture.</h2>
            </div>
            <p>
              Click a node to inspect its details. Pan and zoom are handled by
              React Flow, with dagre providing the initial layout.
            </p>
          </div>

          <GraphCanvas
            nodes={flowGraph.nodes}
            edges={flowGraph.edges}
            selectedNodeId={selectedNodeId}
            onNodeSelect={setSelectedNodeId}
          />
        </article>

        <GraphDetailsPanel node={selectedNode} />
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
