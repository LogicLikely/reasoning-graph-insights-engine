export function DemoPage() {
  return (
    <div className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">Demo Workspace</span>
        <h1>Prepared for graph visualization, without pretending it exists yet.</h1>
        <p>
          This page establishes the interaction shell for the future graph
          experience: a main canvas area, contextual operator notes, and room for
          graph metadata as the frontend moves into visualization work.
        </p>
      </section>

      <section className="demo-grid">
        <article className="demo-stage">
          <div>
            <span className="eyebrow">Graph Canvas Placeholder</span>
            <h2>Visualization surface coming in the next phase.</h2>
            <p>
              React Flow and live graph data are intentionally deferred. This
              panel reserves the visual space and layout behavior they will need.
            </p>
          </div>

          <div className="demo-canvas" aria-label="Graph canvas placeholder">
            <div className="demo-canvas-inner">
              <span className="eyebrow">Reserved Canvas Region</span>
              <p>
                Future graph nodes, edges, and layout controls will render here.
              </p>
              <div className="demo-pillars">
                <span>Graph viewport</span>
                <span>Selection state</span>
                <span>Interaction controls</span>
              </div>
            </div>
          </div>
        </article>

        <aside className="demo-sidebar">
          <div>
            <span className="eyebrow">Operator Panel</span>
            <h3>Context for the future experience</h3>
            <p>
              This sidebar is ready for node details, graph metadata, and
              explanation summaries once live graph interactions land.
            </p>
          </div>

          <ul className="signal-list">
            <li>
              <strong>Upcoming integration</strong>
              <p>Graph read endpoints will supply the initial demo dataset.</p>
            </li>
            <li>
              <strong>Layout intent</strong>
              <p>The canvas remains the focal area while details stay close at hand.</p>
            </li>
            <li>
              <strong>Why a placeholder matters</strong>
              <p>This keeps the SPA feeling deliberate instead of unfinished.</p>
            </li>
          </ul>
        </aside>
      </section>
    </div>
  )
}
