import { Link } from 'react-router-dom'
import './HomePage.css'

export function HomePage() {
  return (
    <div className="page-shell" data-testid="home-page">
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
            Open the demo
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
            arguments, counterarguments, and evidence chains. The Insights Lab
            analysis clearly demonstrates an NP-hard problem where the brute force
            approach is incapable of scaling while a greedy algorithm is effective
            for even very large graphs.
          </p>
        </article>

        <aside className="callout-card">
          <span className="eyebrow">Next Step</span>
          <h3>Exploring graph algorithms</h3>
          <p>
            The demo page shows examples of different graph shapes and sizes.
            Through the "Insights Lab" button, different trends can be
            explored, such as brute force vs greedy algorithms for finding
            the minimal set of counter arguments to effectively rebut a claim.
          </p>
          <Link className="text-link" to="/demo">
            See the demo
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
              <strong>Report on findings</strong>
              <p>
                The project provides the ability to visually compare 
                algorithms with different graph shapes (wide, chain, balanced, 
                diamond) and sizes (100-, 1,000-, 10,000-, and 100,000-node 
                graphs)
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
              <strong>CI/CD</strong>
              <p>The repository is hosted on GitHub. A series of CI gates are run for each
                PR. The frontend and backend have their own gates. Upon merging a PR to
                main, the frontend is automatically deployed to Cloudflare Pages. The backend
                is manually updated.
              </p>
            </li>
          </ul>
        </article>
      </section>
    </div>
  )
}
