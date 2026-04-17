import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <div className="not-found-layout">
      <section className="not-found-card">
        <span className="eyebrow">404</span>
        <h1>Page not found</h1>
        <p>
          The route you requested is not part of the current SPA shell. You can
          head back to the overview or jump directly into the demo workspace.
        </p>

        <div className="hero-actions">
          <Link className="primary-link" to="/">
            Return home
          </Link>
          <Link className="secondary-link" to="/demo">
            Open demo
          </Link>
        </div>
      </section>
    </div>
  )
}
