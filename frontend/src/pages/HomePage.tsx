import { Link } from 'react-router-dom'

export function HomePage() {
  return (
    <div className="page-shell">
      <section className="page-hero">
        <span className="eyebrow">Proof of Concept Platform</span>
        <h1>Map how reasoning structures hold together under pressure.</h1>
        <p>
          Reasoning Graph Insights Engine explores argument networks as graphs so
          teams can inspect support, rebuttal, and structural weak points with
          more clarity than a flat list of claims allows.
        </p>

        <div className="hero-actions">
          <Link className="primary-link" to="/demo">
            Open the demo shell
          </Link>
          <Link className="secondary-link" to="/about">
            Review the architecture
          </Link>
        </div>
      </section>

      <section className="home-showcase">
        <article className="callout-card">
          <span className="eyebrow">What This Explores</span>
          <h2>From claims and evidence to a navigable reasoning surface.</h2>
          <p>
            The project is designed to support graph-based inspection of
            arguments, counterarguments, and evidence chains. This first frontend
            phase establishes the SPA shell that later phases will connect to
            live graph rendering and backend data.
          </p>
        </article>

        <aside className="callout-card">
          <span className="eyebrow">Next Step</span>
          <h3>Demo-ready entry point</h3>
          <p>
            The demo page is already laid out with a future graph canvas and
            operator sidebar so React Flow can slot in naturally in the next
            phase.
          </p>
          <Link className="text-link" to="/demo">
            See the prepared workspace
          </Link>
        </aside>
      </section>

      <section className="section-grid">
        <article className="feature-card">
          <h3>Project purpose</h3>
          <ul className="feature-list">
            <li>
              <strong>Structure-first analysis</strong>
              <p>
                Model how ideas reinforce or challenge each other instead of
                treating reasoning as disconnected text.
              </p>
            </li>
            <li>
              <strong>Inspection-friendly interface</strong>
              <p>
                Prepare a calm, credible workspace for exploring graph state,
                metadata, and explanation layers.
              </p>
            </li>
            <li>
              <strong>Phase-by-phase delivery</strong>
              <p>
                Keep the app easy to extend as routing, visualization, and API
                integration arrive.
              </p>
            </li>
          </ul>
        </article>

        <article className="feature-card">
          <h3>Stack at a glance</h3>
          <ul className="stack-list">
            <li>
              <strong>Frontend</strong>
              <p>React, TypeScript, Vite, and client-side routing.</p>
            </li>
            <li>
              <strong>Backend</strong>
              <p>ASP.NET Core Web API with controller, service, and repository layers.</p>
            </li>
            <li>
              <strong>Near-term direction</strong>
              <p>Graph canvas integration, live endpoint wiring, and deeper analysis views.</p>
            </li>
          </ul>
        </article>
      </section>
    </div>
  )
}
