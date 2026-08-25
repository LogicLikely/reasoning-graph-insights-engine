import './AboutPage.css'

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
            That graph model becomes the basis for visualization and 
            analysis workflows.
          </p>
        </article>

        <article className="architecture-card">
          <h3>Frontend and backend split</h3>
          <p>
            The frontend is responsible for navigation, workspace layout, and
            graph interaction. The backend owns graph retrieval, shaping,
            and the API surface that feeds the demo experience.
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
          <h3>2026 Summer Internship Program</h3>
          <p>
            Developed during Jacob Nuttall’s Summer 2026 software engineering internship 
            project with LogicLikely. Jacob led the counter-set algorithm work and 
            contributed to the full-stack implementation and benchmarking tools.
          </p>
        </article>
      </section>
    </div>
  )
}
