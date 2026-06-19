import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { sampleGraph } from '../fixtures/sampleGraph'
import { DemoPage } from './DemoPage'

const getGraphBySlugMock = vi.fn()
const resetDatabaseMock = vi.fn()

vi.mock('../services/graphService', () => ({
  getDefaultGraphDataSource: () => 'database',
  getGraphBySlug: (slug: string, dataSource: string) => getGraphBySlugMock(slug, dataSource),
  resetDatabase: () => resetDatabaseMock(),
}))

vi.mock('../components/graph/GraphCanvas', () => ({
  GraphCanvas: ({
    onNodeSelect,
    isExpanded,
    onToggleExpanded,
  }: {
    onNodeSelect: (nodeId: string) => void
    isExpanded: boolean
    onToggleExpanded: () => void
  }) => (
    <div data-testid="graph-canvas">
      <button onClick={() => onNodeSelect('E1')} type="button">Select evidence node</button>
      <button onClick={onToggleExpanded} type="button">
        {isExpanded ? 'Restore graph size' : 'Expand graph to viewport'}
      </button>
    </div>
  ),
}))

describe('DemoPage', () => {
  beforeEach(() => {
    getGraphBySlugMock.mockReset()
    resetDatabaseMock.mockReset()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows a loading state while the graph is loading', () => {
    getGraphBySlugMock.mockReturnValue(new Promise(() => {}))

    render(<DemoPage />)

    expect(screen.getByTestId('demo-loading-state')).toBeInTheDocument()
  })

  it('renders the graph on successful load', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    expect(await screen.findByTestId('graph-canvas')).toBeInTheDocument()
    expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'database')
    expect(
      screen.getByRole('heading', { level: 2, name: sampleGraph.title }),
    ).toBeInTheDocument()
  })

  it('shows an error state and retries loading when requested', async () => {
    getGraphBySlugMock
      .mockRejectedValueOnce(new Error('Request failed'))
      .mockResolvedValueOnce(sampleGraph)

    render(<DemoPage />)

    expect(await screen.findByTestId('demo-error-state')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /retry/i }))

    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledTimes(2)
    })

    expect(await screen.findByTestId('graph-canvas')).toBeInTheDocument()
  })

  it('opens node details on selection and allows them to be dismissed', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Select evidence node' }))

    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--open')
    expect(screen.getByRole('region', { name: 'Node details' })).toBeInTheDocument()
    expect(screen.getByText('Photographs from beaches')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Close node details' }))

    expect(screen.getByTestId('demo-details-sheet')).not.toHaveClass('demo-details-sheet--open')
    expect(screen.getByRole('region', { name: 'Node details' })).toBeInTheDocument()
  })

  it('expands the graph to the viewport and restores its original size', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Expand graph to viewport' }))

    expect(screen.getByTestId('demo-graph-stage')).toHaveClass('demo-stage--expanded')
    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--sheet-mode')
    expect(screen.getByTestId('demo-details-sheet').parentElement).toBe(document.body)
    expect(document.body).toHaveStyle({ overflow: 'hidden' })

    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))

    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--open')
    expect(screen.getByText('Photographs from beaches')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Close node details' }))
    fireEvent.click(screen.getByRole('button', { name: 'Restore graph size' }))

    expect(screen.getByTestId('demo-graph-stage')).not.toHaveClass('demo-stage--expanded')
    expect(screen.getByTestId('demo-details-sheet')).not.toHaveClass('demo-details-sheet--sheet-mode')
    expect(document.body).not.toHaveStyle({ overflow: 'hidden' })

    fireEvent.click(screen.getByRole('button', { name: 'Expand graph to viewport' }))
    fireEvent.keyDown(window, { key: 'Escape' })

    expect(screen.getByTestId('demo-graph-stage')).not.toHaveClass('demo-stage--expanded')
  })

  it('confirms and resets the database before reloading the graph', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    getGraphBySlugMock.mockResolvedValue(sampleGraph)
    resetDatabaseMock.mockResolvedValue(undefined)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Reset database' }))

    expect(window.confirm).toHaveBeenCalledWith(
      'Are you sure you want to reset the database? This will restore the default seed data.',
    )

    await waitFor(() => {
      expect(resetDatabaseMock).toHaveBeenCalledTimes(1)
      expect(getGraphBySlugMock).toHaveBeenCalledTimes(2)
    })
  })

  it('does not reset the database when confirmation is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Reset database' }))

    expect(resetDatabaseMock).not.toHaveBeenCalled()
    expect(getGraphBySlugMock).toHaveBeenCalledTimes(1)
  })

  it('switches graph data source and clears node details', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Select evidence node' }))

    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--open')

    fireEvent.click(screen.getByRole('button', { name: 'Fixture' }))

    expect(screen.getByTestId('demo-details-sheet')).not.toHaveClass('demo-details-sheet--open')

    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'fixture')
    })
  })
})
