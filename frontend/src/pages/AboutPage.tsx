export function AboutPage() {
  return (
    <div className="page-shell" data-testid="about-page">
      <section className="page-hero">
        <span className="eyebrow">About The System</span>
        <h1>A small platform for reasoning graphs and structural insight.</h1>
        <p>
          The project treats arguments as connected graph elements so analysis can
          focus on how claims, evidence, and rebuttals interact across the whole
          structure instead of one statement at a time.
        </p>
      </section>

      <section className="about-grid">
        <article className="architecture-card">
          <h3>Reasoning graph concept</h3>
          <p>
            Nodes represent claims, evidence, and counters. Edges capture the
            support or rebuttal relationships that make the structure meaningful.
            That graph model becomes the basis for visualization and later
            analysis workflows.
          </p>
        </article>

        <article className="architecture-card">
          <h3>Frontend and backend split</h3>
          <p>
            The frontend is responsible for navigation, workspace layout, and
            future graph interaction. The backend owns graph retrieval, shaping,
            and the API surface that will feed the demo experience.
          </p>
        </article>
      </section>

      <section className="section-grid">
        <article className="feature-card">
          <h3>Technology choices</h3>
          <ul className="stack-list">
            <li>
              <strong>React + TypeScript</strong>
              <p>Clear page composition and an easy path for richer client behavior.</p>
            </li>
            <li>
              <strong>Vite</strong>
              <p>Fast local iteration and straightforward production builds.</p>
            </li>
            <li>
              <strong>ASP.NET Core backend</strong>
              <p>A clean controller-service-repository structure for graph endpoints.</p>
            </li>
          </ul>
        </article>

        <article className="feature-card">
          <h3>Why this phase stays simple</h3>
          <p>
            This page shell focuses on routing, layout, and credibility. Graph
            rendering and backend integration come next, with a cleaner foundation
            now in place to support them.
          </p>
        </article>
      </section>
    </div>
  )
}
