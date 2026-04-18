import { Link } from 'react-router-dom'

export function Footer() {
  return (
    <footer className="site-footer">
      <div className="site-footer__inner">
        <div className="site-footer__meta">
          <strong>Reasoning Graph Insights Engine</strong>
          <span>Frontend shell prepared for upcoming graph and API phases.</span>
        </div>

        <div className="site-footer__links">
          <Link to="/">Home</Link>
          <Link to="/demo">Demo</Link>
          <Link to="/about">Architecture</Link>
        </div>
      </div>
    </footer>
  )
}
