import { Link } from 'react-router-dom'
import './AboutPage.css'

const repositoryUrl =
  'https://github.com/LogicLikely/reasoning-graph-insights-engine'

export function AboutPage() {
  return (
    <div className="page-shell about-page" data-testid="about-page">
      <section className="page-hero about-hero">
        <span className="eyebrow">About the project</span>
        <h1>A working laboratory for reasoning graphs.</h1>
        <p>
          The Reasoning Graph Insights Engine is a full-stack proof of concept
          from LogicLikely. It models claims, evidence, and objections as a
          directed graph, then combines interactive visualization with Bayesian
          analysis, counter-set search, sensitivity analysis, and repeatable
          performance reporting.
        </p>
      </section>

      <section
        className="internship-callout"
        aria-labelledby="internship-heading"
      >
        <div className="internship-callout__heading">
          <span className="about-kicker">
            Summer 2026 software engineering internship
          </span>
          <h2 id="internship-heading">A collaborative engineering project.</h2>
        </div>

        <div className="internship-callout__copy">
          <p>
            This proof of concept was developed collaboratively during{' '}
            <a
              href="https://github.com/Jacobn99"
              target="_blank"
              rel="noreferrer"
            >
              Jacob Nuttall’s
            </a>{' '}
            Summer 2026 software engineering internship with LogicLikely. Jacob
            led the initial research and implementation of the
            minimal-counter-set analysis and authored the graph-pruning,
            Bayes-factor, and posterior-odds calculations that underpin the
            current solver. He also contributed to the React and ASP.NET Core
            integration, automated tests, and benchmarking tools.
          </p>
          <p>
            The repository and production demo reflect collaborative review,
            refinement, and deployment throughout the project.
          </p>
        </div>
      </section>

      <section className="about-section" aria-labelledby="purpose-heading">
        <div className="about-section__heading">
          <span className="eyebrow">Why this exists</span>
          <h2 id="purpose-heading">
            Reasoning becomes richer when its relationships stay visible.
          </h2>
        </div>

        <div className="about-purpose">
          <div className="about-purpose__copy">
            <p>
              LogicLikely is exploring structured reasoning systems as one way
              to improve online discourse. This project asks what becomes
              visible when an argument is examined as a connected structure
              instead of a flat list of statements.
            </p>
            <p>
              The aim is not to decide which claims are true. It is to make the
              structure inspectable: how support and objections interact, where
              modeled confidence is sensitive, and what changes could alter a
              target claim.
            </p>
          </div>

          <ul className="graph-language" aria-label="Reasoning graph elements">
            <li>
              <strong>Claims</strong>
              <span>Statements under examination</span>
            </li>
            <li>
              <strong>Evidence and objections</strong>
              <span>Information that supports or challenges a claim</span>
            </li>
            <li>
              <strong>Relationships</strong>
              <span>Directed connections that make analysis possible</span>
            </li>
          </ul>
        </div>
      </section>

      <section className="about-section" aria-labelledby="architecture-heading">
        <div className="about-section__heading">
          <span className="eyebrow">Architecture</span>
          <h2 id="architecture-heading">
            From edge delivery to graph analysis.
          </h2>
          <p>
            Cloudflare Pages serves the React client, which handles navigation,
            visualization, and editing in the browser. Over HTTPS, the client
            exchanges REST / JSON requests with an ASP.NET Core API that
            coordinates graph analysis, persistence, and performance reporting.
          </p>
        </div>

        <figure
          className="architecture-diagram"
          aria-labelledby="architecture-diagram-caption"
          aria-describedby="architecture-diagram-summary"
        >
          <figcaption id="architecture-diagram-caption">
            Production request and data flow
          </figcaption>
          <p
            id="architecture-diagram-summary"
            className="architecture-diagram__summary"
          >
            Cloudflare Pages delivers the React application to the browser. The
            application communicates with the ASP.NET Core API, which delegates
            to backend services and returns the results through the same API.
          </p>

          <div className="architecture-runtime">
            <section
              className="architecture-zone architecture-zone--frontend"
              aria-label="Client and frontend delivery"
            >
              <span className="architecture-zone__label">Client &amp; delivery</span>

              <div className="architecture-delivery-flow">
                <article className="architecture-system">
                  <div className="architecture-system__heading">
                    <span
                      className="architecture-marker architecture-marker--cloudflare"
                      aria-hidden="true"
                    >
                      CF
                    </span>
                    <div>
                      <span>Frontend hosting</span>
                      <strong>Cloudflare Pages</strong>
                    </div>
                  </div>
                  <p>Serves the production build at the edge</p>
                </article>

                <div className="architecture-delivery-connector" aria-hidden="true">
                  <span>Delivers app</span>
                </div>

                <article className="architecture-system architecture-system--client">
                  <div className="architecture-system__heading">
                    <span className="architecture-marker" aria-hidden="true">
                      UI
                    </span>
                    <div>
                      <span>Browser interface</span>
                      <strong>React + TypeScript</strong>
                    </div>
                  </div>
                  <p>Graph navigation, visualization, and editing</p>
                </article>
              </div>
            </section>

            <div
              className="architecture-network"
              aria-label="HTTPS REST and JSON connection through the Cloudflare edge"
            >
              <span>Cloudflare edge · HTTPS</span>
              <strong>REST / JSON</strong>
            </div>

            <section
              className="architecture-zone architecture-zone--backend"
              aria-label="Application backend"
            >
              <span className="architecture-zone__label">Application backend</span>

              <div className="architecture-backbone">
                <article className="architecture-system">
                  <div className="architecture-system__heading">
                    <span className="architecture-marker" aria-hidden="true">
                      API
                    </span>
                    <div>
                      <span>Application boundary</span>
                      <strong>ASP.NET Core API</strong>
                    </div>
                  </div>
                  <p>Controllers and REST endpoints</p>
                </article>

                <div className="architecture-backbone__connector" aria-hidden="true" />

                <article className="architecture-system architecture-system--hub">
                  <div className="architecture-system__heading">
                    <span className="architecture-marker" aria-hidden="true">
                      SVC
                    </span>
                    <div>
                      <span>Service layer</span>
                      <strong>Backend services</strong>
                    </div>
                  </div>
                  <p>Coordinates analysis, persistence, and reporting</p>
                </article>
              </div>

              <ul className="architecture-responsibilities">
                <li className="architecture-responsibility">
                  <div className="architecture-responsibility__heading">
                    <span className="architecture-marker" aria-hidden="true">
                      ALG
                    </span>
                    <div>
                      <span>Calculation</span>
                      <strong>Analysis engine</strong>
                    </div>
                  </div>
                  <p>
                    Bayesian evaluation, counter-set search, and sensitivity
                    analysis
                  </p>
                </li>

                <li className="architecture-responsibility">
                  <div className="architecture-responsibility__heading">
                    <span className="architecture-marker" aria-hidden="true">
                      DATA
                    </span>
                    <div>
                      <span>Persistence</span>
                      <strong>Dapper repository</strong>
                    </div>
                  </div>
                  <p>Graph reads and writes</p>
                  <div className="architecture-store">
                    <span className="architecture-store__icon" aria-hidden="true">
                      DB
                    </span>
                    <div>
                      <span>Graph database</span>
                      <strong>PostgreSQL</strong>
                    </div>
                  </div>
                </li>

                <li className="architecture-responsibility">
                  <div className="architecture-responsibility__heading">
                    <span className="architecture-marker" aria-hidden="true">
                      RUN
                    </span>
                    <div>
                      <span>Reporting</span>
                      <strong>Performance run store</strong>
                    </div>
                  </div>
                  <p>Benchmark capture and trend history</p>
                  <div className="architecture-store">
                    <span className="architecture-store__icon" aria-hidden="true">
                      JSON
                    </span>
                    <div>
                      <span>Report store</span>
                      <strong>JSON run history</strong>
                    </div>
                  </div>
                </li>
              </ul>
            </section>
          </div>
        </figure>
      </section>

      <section className="about-section" aria-labelledby="explores-heading">
        <div className="about-section__heading">
          <span className="eyebrow">What it explores</span>
          <h2 id="explores-heading">From graph structure to measurable behavior.</h2>
        </div>

        <div className="capability-grid">
          <article className="capability-card">
            <span className="capability-card__number">01</span>
            <h3>Bayesian graph evaluation</h3>
            <p>
              Prunes a target-relevant directed acyclic graph to compatible
              evidence paths, calculates a Bayes factor, and applies it to the
              target claim’s prior log odds.
            </p>
          </article>

          <article className="capability-card">
            <span className="capability-card__number">02</span>
            <h3>Counter-set search</h3>
            <p>
              Explores a minimum-counter-set problem motivated by NP-hard
              set-cover optimization. Insights Lab compares a fast greedy
              heuristic with a time-bounded exhaustive reference.
            </p>
          </article>

          <article className="capability-card">
            <span className="capability-card__number">03</span>
            <h3>Evidence sensitivity</h3>
            <p>
              Removes evidence one item at a time, recalculates the model, and
              ranks evidence and graph nodes by the resulting change in modeled
              confidence.
            </p>
          </article>

          <article className="capability-card">
            <span className="capability-card__number">04</span>
            <h3>Performance analysis</h3>
            <p>
              Records timing, CPU use, managed allocations, graph metadata, and
              outcomes across deterministic graph shapes and sizes.
            </p>
          </article>
        </div>
      </section>

      <section className="about-notes" aria-label="Engineering context">
        <article className="tradeoff-card">
          <span className="about-kicker">Engineering tradeoffs</span>
          <h2>Designed to make tradeoffs visible.</h2>
          <p>
            The project pairs a practical greedy solver with an exhaustive
            reference, generates deterministic stress graphs, and preserves
            benchmark history for comparison. A completed exhaustive search can
            establish minimum cardinality; if its server-owned time budget
            expires, the result is explicitly marked partial and unproven.
          </p>
          <p>
            Together, the application demonstrates algorithm design,
            approximate-versus-exhaustive search, full-stack integration,
            automated verification, performance measurement, and production
            deployment.
          </p>
        </article>

        <article className="scope-card">
          <span className="about-kicker">Scope</span>
          <h2>What the results mean</h2>
          <p>
            Insights reports behavior within the current model. It can show how
            configured claims, evidence, priors, and relationships respond to
            structural changes, but it does not determine whether a claim is
            true or whether a piece of evidence is reliable.
          </p>
        </article>
      </section>

      <section className="about-cta" aria-labelledby="explore-heading">
        <div className="about-cta__heading">
          <span className="eyebrow">Explore the work</span>
          <h2 id="explore-heading">
            See the model in action—or inspect the implementation.
          </h2>
        </div>

        <div className="about-cta__copy">
          <p>
            The interactive demo provides graph examples and saved performance
            trends. The public repository contains the implementation,
            technical documentation, automated tests, and local development
            instructions.
          </p>
        </div>

        <div className="hero-actions">
          <Link className="primary-link" to="/demo">
            Open the demo
          </Link>
          <a
            className="secondary-link"
            href={repositoryUrl}
            target="_blank"
            rel="noreferrer"
          >
            View source on GitHub
            <span aria-hidden="true">↗</span>
          </a>
        </div>
      </section>
    </div>
  )
}
