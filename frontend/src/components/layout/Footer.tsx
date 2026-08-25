import { Link } from 'react-router-dom'

export function Footer() {
  return (
    <footer className="site-footer">
      <div className="site-footer__inner">
        <div className="site-footer__meta">
          <strong>Reasoning Graph Insights Engine</strong>
          <span>Exploring NP-hard optimization in reasoning graphs.</span>
        </div>

        <div className="site-footer__links">
          <Link to="/">Home</Link>
          <Link to="/demo">Demo</Link>
          <Link to="/about">About</Link>
        </div>
      </div>
    </footer>
  )
}
