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
        renderer="standard"
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
        renderer="standard"
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
        renderer="standard"
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
        renderer="standard"
        onResetDatabase={onResetDatabase}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: 'Reset database' }))

    expect(onResetDatabase).toHaveBeenCalledTimes(1)
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
        renderer="standard"
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
        renderer="standard"
        onDataSourceChange={onDataSourceChange}
      />,
    )

    expect(screen.getByRole('button', { name: 'Database' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Fixture' })).toHaveAttribute('aria-pressed', 'false')

    fireEvent.click(screen.getByRole('button', { name: 'Fixture' }))

    expect(onDataSourceChange).toHaveBeenCalledWith('fixture')
  })

  it('allows switching between standard and compact renderers', () => {
    const onRendererChange = vi.fn()

    render(
      <GraphOverviewPanel
        title="Sample Reasoning Graph"
        description="A database graph description."
        nodeCount={10}
        edgeCount={9}
        fixtureName="sample-medium"
        dataSource="database"
        renderer="standard"
        onRendererChange={onRendererChange}
      />,
    )

    const rendererGroup = screen.getByRole('group', { name: 'Graph renderer' })
    expect(rendererGroup).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Standard' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Compact' })).toHaveAttribute('aria-pressed', 'false')

    fireEvent.click(screen.getByRole('button', { name: 'Compact' }))

    expect(onRendererChange).toHaveBeenCalledWith('compact')
  })
})
