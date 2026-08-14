import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { sampleGraph } from '../fixtures/sampleGraph'
import { DemoPage } from './DemoPage'

const getGraphBySlugMock = vi.fn()
const resetDatabaseMock = vi.fn()
const addNodeMock = vi.fn()
const deleteNodeMock = vi.fn()
const updateNodeMock = vi.fn()
const getEvidenceImpactRankingMock = vi.fn()

vi.mock('../services/graphService', () => ({
  addNode: (...args: unknown[]) => addNodeMock(...args),
  deleteNode: (...args: unknown[]) => deleteNodeMock(...args),
  getDefaultGraphDataSource: () => 'database',
  getEvidenceImpactRanking: (...args: unknown[]) => getEvidenceImpactRankingMock(...args),
  getGraphBySlug: (slug: string, dataSource: string) => getGraphBySlugMock(slug, dataSource),
  resetDatabase: () => resetDatabaseMock(),
  updateNode: (...args: unknown[]) => updateNodeMock(...args),
}))

vi.mock('../components/graph/InsightsGraphCanvas', () => ({
  InsightsGraphCanvas: ({
    graph,
    onNodeSelect,
    isFullscreen,
    onFullscreenChange,
  }: {
    graph: typeof sampleGraph
    onNodeSelect: (node: (typeof sampleGraph.nodes)[number] | null) => void
    isFullscreen: boolean
    onFullscreenChange: (isFullscreen: boolean) => void
  }) => (
    <div data-testid="insights-graph-canvas">
      <button
        onClick={() => onNodeSelect(graph.nodes.find((node) => node.id === 'E1')!)}
        type="button"
      >
        Select evidence node
      </button>
      <button onClick={() => onFullscreenChange(!isFullscreen)} type="button">
        {isFullscreen ? 'Restore graph size' : 'Expand graph to viewport'}
      </button>
    </div>
  ),
}))

describe('DemoPage', () => {
  beforeEach(() => {
    getGraphBySlugMock.mockReset()
    resetDatabaseMock.mockReset()
    addNodeMock.mockReset()
    deleteNodeMock.mockReset()
    updateNodeMock.mockReset()
    getEvidenceImpactRankingMock.mockReset()
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

    expect(await screen.findByTestId('insights-graph-canvas')).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Graph renderer' })).not.toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Graph data source' })).toBeInTheDocument()
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

    expect(await screen.findByTestId('insights-graph-canvas')).toBeInTheDocument()
  })

  it('keeps the overview panel and data source controls available when database loading fails', async () => {
    getGraphBySlugMock
      .mockRejectedValueOnce(new Error('Request failed'))
      .mockResolvedValueOnce(sampleGraph)

    render(<DemoPage />)

    expect(await screen.findByTestId('demo-error-state')).toBeInTheDocument()
    expect(screen.getByTestId('graph-overview-panel')).toBeInTheDocument()
    expect(screen.getAllByRole('heading', { level: 3, name: 'Unable to load graph' })).toHaveLength(2)
    expect(screen.getAllByText('Unable to load graph data right now.')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Database' })).toHaveAttribute('aria-pressed', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'Fixture' }))

    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'fixture')
    })

    expect(await screen.findByTestId('insights-graph-canvas')).toBeInTheDocument()
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

  it('keeps the graph available when switching data sources and clears stale details', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    await screen.findByTestId('insights-graph-canvas')
    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))
    fireEvent.click(screen.getByRole('button', { name: 'Fixture' }))

    expect(screen.getByTestId('demo-details-sheet')).not.toHaveClass('demo-details-sheet--open')

    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'fixture')
      expect(screen.getByTestId('insights-graph-canvas')).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: 'Fixture' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('preserves selection across a database mutation reload', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)
    updateNodeMock.mockResolvedValue(undefined)

    render(<DemoPage />)

    await screen.findByTestId('insights-graph-canvas')
    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))
    fireEvent.click(screen.getByRole('button', {
      name: /Edit this node's title, type, likelihood, and description/i,
    }))
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => {
      expect(updateNodeMock).toHaveBeenCalled()
      expect(getGraphBySlugMock).toHaveBeenCalledTimes(2)
    })

    expect(screen.getByTestId('insights-graph-canvas')).toBeInTheDocument()
    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--open')
    expect(screen.getByText('Photographs from beaches')).toBeInTheDocument()
  })

  it('prints the evidence impact ranking when e is pressed with a selected node', async () => {
    const consoleLogSpy = vi.spyOn(console, 'log').mockImplementation(() => undefined)
    getGraphBySlugMock.mockResolvedValue(sampleGraph)
    getEvidenceImpactRankingMock.mockResolvedValue({
      supportingEvidence: [
        { nodeId: 'C1', logLr: 1, probabilityDifference: 0.2 },
        { nodeId: 'E2', logLr: 0.52, probabilityDifference: 0.1 },
        { nodeId: 'E1', logLr: 0.47, probabilityDifference: 0.09 },
      ],
      counterEvidence: [
        { nodeId: 'O3', logLr: -1.87, probabilityDifference: -0.3 },
        { nodeId: 'O2', logLr: -1.53, probabilityDifference: -0.25 },
        { nodeId: 'O1', logLr: -1.49, probabilityDifference: -0.2 },
      ],
    })

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Select evidence node' }))
    fireEvent.keyDown(window, { key: 'e' })

    await waitFor(() => {
      expect(getEvidenceImpactRankingMock).toHaveBeenCalledWith('sample-medium', 'E1', 'database')
      expect(consoleLogSpy).toHaveBeenCalledWith({
        supportingEvidence: [
          { nodeId: 'E2', logLr: 0.52, probabilityDifference: 0.1 },
          { nodeId: 'E1', logLr: 0.47, probabilityDifference: 0.09 },
        ],
        counterEvidence: [
          { nodeId: 'O2', logLr: -1.53, probabilityDifference: -0.25 },
          { nodeId: 'O1', logLr: -1.49, probabilityDifference: -0.2 },
        ],
      })
    })
  })

  it('keeps page details in sync with GraphMap fullscreen requests', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Expand graph to viewport' }))

    expect(screen.getByTestId('demo-graph-stage')).not.toHaveClass('demo-stage--expanded')
    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--sheet-mode')
    expect(screen.getByTestId('demo-details-sheet').parentElement).toBe(document.body)

    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))

    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--open')
    expect(screen.getByText('Photographs from beaches')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Close node details' }))
    fireEvent.click(screen.getByRole('button', { name: 'Restore graph size' }))

    expect(screen.getByTestId('demo-graph-stage')).not.toHaveClass('demo-stage--expanded')
    expect(screen.getByTestId('demo-details-sheet')).not.toHaveClass('demo-details-sheet--sheet-mode')

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

  it('shows fixture mode alert instead of updating a node', async () => {
    vi.spyOn(window, 'alert').mockImplementation(() => undefined)
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Fixture' }))
    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'fixture')
    })

    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))
    fireEvent.click(screen.getByRole('button', { name: /Edit this node's title, type, likelihood, and description/i }))
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    expect(window.alert).toHaveBeenCalledWith('This feature is not available in fixture mode.')
    expect(updateNodeMock).not.toHaveBeenCalled()
  })

  it('shows fixture mode alert instead of adding a node', async () => {
    vi.spyOn(window, 'alert').mockImplementation(() => undefined)
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Fixture' }))
    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'fixture')
    })

    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))
    fireEvent.click(screen.getByRole('button', { name: 'Add a child node connected to this selected node' }))
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Fixture add' } })
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Fixture add body' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create Node' }))

    expect(window.alert).toHaveBeenCalledWith('This feature is not available in fixture mode.')
    expect(addNodeMock).not.toHaveBeenCalled()
  })

  it('shows fixture mode alert after delete confirmation without deleting a node', async () => {
    vi.spyOn(window, 'alert').mockImplementation(() => undefined)
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Fixture' }))
    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'fixture')
    })

    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))
    fireEvent.click(screen.getByRole('button', { name: 'Delete this node from the graph' }))

    expect(window.confirm).toHaveBeenCalledWith('Are you sure you want to delete this node? This action cannot be undone.')
    expect(window.alert).toHaveBeenCalledWith('This feature is not available in fixture mode.')
    expect(deleteNodeMock).not.toHaveBeenCalled()
  })
})
