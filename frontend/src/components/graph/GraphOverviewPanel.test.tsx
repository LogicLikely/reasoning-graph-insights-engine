import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GraphOverviewPanel } from './GraphOverviewPanel'

describe('GraphOverviewPanel', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('renders graph stats in a separate overview panel', () => {
    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A local fixture description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="fixture"
      />,
    )

    expect(screen.getByTestId('graph-overview-panel')).toBeInTheDocument()
    expect(screen.getByText('Graph Overview')).toBeInTheDocument()
    expect(screen.getByText('Sample Reasoning Graph')).toBeInTheDocument()
    expect(screen.getByText('A local fixture description.')).toBeInTheDocument()
    expect(screen.getByText('10')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument()
    expect(screen.getByText('sample-medium')).toBeInTheDocument()
  })

  it('labels the data source as Fixture when the fixture env is true', () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'true')

    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A local fixture description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="fixture"
      />,
    )

    expect(screen.getByRole('button', { name: 'Fixture' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('labels the data source as Database when the fixture env is false', () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'false')

    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A database graph description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="database"
      />,
    )

    expect(screen.getByRole('button', { name: 'Database' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('shows the reset database button when the database is active', () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'false')
    const onResetDatabase = vi.fn()

    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A database graph description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="database"
        onResetDatabase={onResetDatabase}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Reset database' }))

    expect(onResetDatabase).toHaveBeenCalledTimes(1)
  })

  it('opens the Insights Lab when its button is pressed', () => {
    const onOpenInsightsLab = vi.fn()

    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A local fixture description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="fixture"
        onOpenInsightsLab={onOpenInsightsLab}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Insights Lab' }))

    expect(onOpenInsightsLab).toHaveBeenCalledTimes(1)
  })

  it('hides the reset database button when the fixture is active', () => {
    vi.stubEnv('VITE_USE_FIXTURE', 'true')

    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A local fixture description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="fixture"
        onResetDatabase={vi.fn()}
      />,
    )

    expect(screen.queryByRole('button', { name: 'Reset database' })).not.toBeInTheDocument()
  })

  it('allows switching between fixture and database sources', () => {
    const onDataSourceChange = vi.fn()

    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A database graph description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="database"
        onDataSourceChange={onDataSourceChange}
      />,
    )

    expect(screen.getByRole('button', { name: 'Database' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Fixture' })).toHaveAttribute('aria-pressed', 'false')

    fireEvent.click(screen.getByRole('button', { name: 'Fixture' }))

    expect(onDataSourceChange).toHaveBeenCalledWith('fixture')
  })

  it('lists database graphs by title and slug and reports selection changes', () => {
    const onGraphChange = vi.fn()

    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A database graph description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="database"
        graphs={[
          {
            slug: 'sample-medium',
            title: 'Sample Reasoning Graph',
            description: 'First graph',
            nodeCount: 10,
            edgeCount: 9,
          },
          {
            slug: 'flat-earth-large',
            title: 'Large Flat-Earth Reasoning Graph',
            description: 'Second graph',
            nodeCount: 1_000,
            edgeCount: 1_248,
          },
        ]}
        selectedGraphSlug="sample-medium"
        onGraphChange={onGraphChange}
      />,
    )

    const selector = screen.getByRole('combobox', { name: 'Database graph' })
    expect(selector).toHaveValue('sample-medium')
    expect(screen.getByRole('option', {
      name: 'Large Flat-Earth Reasoning Graph — 1,000 nodes, 1,248 edges — flat-earth-large',
    }))
      .toBeInTheDocument()

    fireEvent.change(selector, { target: { value: 'flat-earth-large' } })

    expect(onGraphChange).toHaveBeenCalledWith('flat-earth-large')
  })

  it('disables graph selection while a database reset is pending', () => {
    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A database graph description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="database"
        graphs={[
          {
            slug: 'sample-medium',
            title: 'Sample Reasoning Graph',
            description: 'First graph',
            nodeCount: 10,
            edgeCount: 9,
          },
        ]}
        selectedGraphSlug="sample-medium"
        isResettingDatabase
      />,
    )

    expect(screen.getByRole('combobox', { name: 'Database graph' })).toBeDisabled()
  })

  it('shows a disabled empty selector when the database catalog has no graphs', () => {
    render(
      <GraphOverviewPanel
        title="No database graphs"
        description="The database is empty."
        nodeCount={0}
        edgeCount={0}
        fixtureName="No database graphs"
        dataSource="database"
        graphs={[]}
      />,
    )

    expect(screen.getByRole('combobox', { name: 'Database graph' })).toBeDisabled()
    expect(screen.getByRole('option', { name: 'No graphs available' })).toBeInTheDocument()
  })
})
