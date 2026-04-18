import { NavLink } from 'react-router-dom'

const navItems = [
  { to: '/', label: 'Home', end: true },
  { to: '/demo', label: 'Demo' },
  { to: '/about', label: 'About' },
]

export function Header() {
  return (
    <header className="site-header">
      <div className="site-header__inner">
        <NavLink className="brand-link" to="/">
          <span className="brand-mark" aria-hidden="true">
            RG
          </span>
          <span className="brand-copy">
            <span className="brand-title">Reasoning Graph Insights</span>
            <span className="brand-subtitle">
              Structure-aware analysis for argument networks
            </span>
          </span>
        </NavLink>

        <nav className="site-nav" aria-label="Primary">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                `site-nav__link${isActive ? ' site-nav__link--active' : ''}`
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
      </div>
    </header>
  )
}
