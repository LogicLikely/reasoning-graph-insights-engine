import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { AboutPage } from './AboutPage'

function renderAboutPage() {
  return render(
    <MemoryRouter>
      <AboutPage />
    </MemoryRouter>,
  )
}

describe('AboutPage', () => {
  it('presents the project, internship contribution, and architecture', () => {
    renderAboutPage()

    expect(
      screen.getByRole('heading', {
        name: /A working laboratory for reasoning graphs/i,
        level: 1,
      }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('heading', {
        name: /A collaborative engineering project/i,
        level: 2,
      }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('link', { name: /Jacob Nuttall/i }),
    ).toHaveAttribute('href', 'https://github.com/Jacobn99')
    expect(
      screen.getByRole('heading', {
        name: /A full-stack path from interaction to analysis/i,
        level: 2,
      }),
    ).toBeInTheDocument()
    expect(screen.getByText('React + TypeScript')).toBeInTheDocument()
    expect(screen.getByText('ASP.NET Core API')).toBeInTheDocument()
    expect(screen.getByText('PostgreSQL')).toBeInTheDocument()
  })

  it('links to the demo and public source repository', () => {
    renderAboutPage()

    expect(screen.getByRole('link', { name: /Open the demo/i })).toHaveAttribute(
      'href',
      '/demo',
    )
    expect(
      screen.getByRole('link', { name: /View source on GitHub/i }),
    ).toHaveAttribute(
      'href',
      'https://github.com/LogicLikely/reasoning-graph-insights-engine',
    )
  })
})
