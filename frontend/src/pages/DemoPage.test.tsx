import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { sampleGraph } from '../fixtures/sampleGraph'
import { DemoPage } from './DemoPage'

const getGraphBySlugMock = vi.fn()
const getGraphCatalogMock = vi.fn()
const resetDatabaseMock = vi.fn()
const addEdgeMock = vi.fn()
const addNodeMock = vi.fn()
const deleteNodeMock = vi.fn()
const getBoundedNodeCounterSetMock = vi.fn()
const getNodeCounterSetMock = vi.fn()
const updateEdgeMock = vi.fn()
const updateNodeMock = vi.fn()
const getEvidenceImpactRankingMock = vi.fn()
const getLeastRobustNodeMock = vi.fn()
const getNodeRobustnessRankingMock = vi.fn()

const graphCatalog = [
  {
    slug: sampleGraph.slug,
    title: sampleGraph.title,
    description: sampleGraph.description,
    nodeCount: sampleGraph.nodes.length,
    edgeCount: sampleGraph.edges.length,
  },
  {
    slug: 'flat-earth-large',
    title: 'Large Flat-Earth Reasoning Graph',
    description: 'A larger database graph.',
    nodeCount: 1_000,
    edgeCount: 1_248,
  },
]

const largeGraph = {
  ...sampleGraph,
  slug: 'flat-earth-large',
  title: 'Large Flat-Earth Reasoning Graph',
  description: 'A larger database graph.',
}

const balancedStressGraphSummary = {
  slug: 'stress-balanced-1k',
  title: 'Balanced tree (1,000 nodes)',
  description: 'A balanced tree stress graph.',
  nodeCount: 1_000,
  edgeCount: 999,
}

const balancedStressGraph = {
  ...sampleGraph,
  slug: balancedStressGraphSummary.slug,
  title: balancedStressGraphSummary.title,
  description: balancedStressGraphSummary.description,
}

const graphCatalogWithStressGraph = [...graphCatalog, balancedStressGraphSummary]

function createDeferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })

  return { promise, resolve }
}

vi.mock('../services/graphService', () => ({
  addEdge: (...args: unknown[]) => addEdgeMock(...args),
  addNode: (...args: unknown[]) => addNodeMock(...args),
  deleteNode: (...args: unknown[]) => deleteNodeMock(...args),
  getBoundedNodeCounterSet: (...args: unknown[]) => getBoundedNodeCounterSetMock(...args),
  getDefaultGraphDataSource: () => 'database',
  getEvidenceImpactRanking: (...args: unknown[]) => getEvidenceImpactRankingMock(...args),
  getLeastRobustNode: (...args: unknown[]) => getLeastRobustNodeMock(...args),
  getNodeRobustnessRanking: (...args: unknown[]) => getNodeRobustnessRankingMock(...args),
  getGraphCatalog: () => getGraphCatalogMock(),
  getGraphBySlug: (slug: string, dataSource: string) => getGraphBySlugMock(slug, dataSource),
  getNodeCounterSet: (...args: unknown[]) => getNodeCounterSetMock(...args),
  resetDatabase: (...args: unknown[]) => resetDatabaseMock(...args),
  updateEdge: (...args: unknown[]) => updateEdgeMock(...args),
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

vi.mock('../components/graph/InsightsLabDialog', () => ({
  InsightsLabDialog: ({
    graph,
    graphDataSource,
    isOpen,
    onClose,
    onGraphUpdated,
    selectedNodeId,
  }: {
    graph: typeof sampleGraph | null
    graphDataSource: 'fixture' | 'database'
    isOpen: boolean
    onClose: () => void
    onGraphUpdated?: () => void
    selectedNodeId?: string
  }) => isOpen ? (
    <div aria-label="Insights Lab" data-testid="insights-lab-dialog" role="dialog">
      <span data-testid="insights-lab-context">
        {[graph?.slug ?? 'no-graph', graphDataSource, selectedNodeId ?? 'no-selection'].join('|')}
      </span>
      <button onClick={onGraphUpdated} type="button">Simulate graph update</button>
      <button onClick={onClose} type="button">Close Insights Lab</button>
    </div>
  ) : null,
}))

describe('DemoPage', () => {
  beforeEach(() => {
    getGraphBySlugMock.mockReset()
    getGraphCatalogMock.mockReset()
    getGraphCatalogMock.mockResolvedValue(graphCatalog)
    resetDatabaseMock.mockReset()
    addEdgeMock.mockReset()
    addNodeMock.mockReset()
    deleteNodeMock.mockReset()
    getBoundedNodeCounterSetMock.mockReset()
    getNodeCounterSetMock.mockReset()
    updateEdgeMock.mockReset()
    updateNodeMock.mockReset()
    getEvidenceImpactRankingMock.mockReset()
    getLeastRobustNodeMock.mockReset()
    getNodeRobustnessRankingMock.mockReset()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows a loading state while the graph is loading', () => {
    getGraphCatalogMock.mockReturnValue(new Promise(() => {}))
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

  it('opens and closes the Insights Lab with the active graph context', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Select evidence node' }))
    fireEvent.click(screen.getByRole('button', { name: 'Insights Lab' }))

    expect(screen.getByRole('dialog', { name: 'Insights Lab' })).toBeInTheDocument()
    expect(screen.getByTestId('insights-lab-context')).toHaveTextContent(
      'sample-medium|database|E1',
    )

    fireEvent.click(screen.getByRole('button', { name: 'Close Insights Lab' }))

    expect(screen.queryByRole('dialog', { name: 'Insights Lab' })).not.toBeInTheDocument()
  })

  it('reloads the active graph when the Insights Lab reports an update', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    await screen.findByTestId('insights-graph-canvas')
    fireEvent.click(screen.getByRole('button', { name: 'Insights Lab' }))
    fireEvent.click(screen.getByRole('button', { name: 'Simulate graph update' }))

    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledTimes(2)
    })
  })

  it('does not launch analyses from the removed I, B, E, R, or J shortcuts', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Select evidence node' }))
    for (const key of ['i', 'b', 'e', 'r', 'j']) {
      fireEvent.keyDown(window, { key })
    }

    expect(getNodeCounterSetMock).not.toHaveBeenCalled()
    expect(getBoundedNodeCounterSetMock).not.toHaveBeenCalled()
    expect(getEvidenceImpactRankingMock).not.toHaveBeenCalled()
    expect(getLeastRobustNodeMock).not.toHaveBeenCalled()
    expect(getNodeRobustnessRankingMock).not.toHaveBeenCalled()
  })

  it('gates page keyboard shortcuts while the Insights Lab is open', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Select evidence node' }))
    fireEvent.click(screen.getByRole('button', { name: 'Insights Lab' }))
    fireEvent.keyDown(window, { key: 'a' })
    fireEvent.keyDown(window, { key: 'd' })
    fireEvent.keyDown(window, { key: 'Escape' })

    expect(addNodeMock).not.toHaveBeenCalled()
    expect(deleteNodeMock).not.toHaveBeenCalled()
    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--open')

    fireEvent.click(screen.getByRole('button', { name: 'Close Insights Lab' }))
    fireEvent.keyDown(window, { key: 'Escape' })

    expect(screen.getByTestId('demo-details-sheet')).not.toHaveClass('demo-details-sheet--open')
  })

  it('selects a database graph and uses its slug for mutations', async () => {
    getGraphBySlugMock.mockImplementation((slug: string) => (
      Promise.resolve(slug === largeGraph.slug ? largeGraph : sampleGraph)
    ))
    updateNodeMock.mockResolvedValue(undefined)

    render(<DemoPage />)

    await screen.findByTestId('insights-graph-canvas')
    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))
    expect(screen.getByTestId('demo-details-sheet')).toHaveClass('demo-details-sheet--open')

    fireEvent.change(screen.getByRole('combobox', { name: 'Database graph' }), {
      target: { value: largeGraph.slug },
    })

    expect(screen.getByTestId('demo-details-sheet')).not.toHaveClass('demo-details-sheet--open')
    expect(await screen.findByRole('heading', { level: 2, name: largeGraph.title })).toBeInTheDocument()
    expect(getGraphBySlugMock).toHaveBeenCalledWith(largeGraph.slug, 'database')

    fireEvent.click(screen.getByRole('button', { name: 'Select evidence node' }))
    fireEvent.click(screen.getByRole('button', {
      name: /Edit this node's title, type, likelihood, and description/i,
    }))
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => {
      expect(updateNodeMock).toHaveBeenCalledWith(largeGraph.slug, 'E1', expect.any(Object))
    })
  })

  it('remembers the selected database graph while fixture mode is active', async () => {
    getGraphBySlugMock.mockImplementation((slug: string, dataSource: string) => (
      Promise.resolve(dataSource === 'database' && slug === largeGraph.slug ? largeGraph : sampleGraph)
    ))

    render(<DemoPage />)

    await screen.findByTestId('insights-graph-canvas')
    fireEvent.change(screen.getByRole('combobox', { name: 'Database graph' }), {
      target: { value: largeGraph.slug },
    })
    await screen.findByRole('heading', { level: 2, name: largeGraph.title })

    fireEvent.click(screen.getByRole('button', { name: 'Fixture' }))
    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'fixture')
    })
    expect(screen.queryByRole('combobox', { name: 'Database graph' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Database' }))

    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenLastCalledWith(largeGraph.slug, 'database')
    })
    expect(screen.getByRole('combobox', { name: 'Database graph' })).toHaveValue(largeGraph.slug)
  })

  it('ignores a stale graph response after the selected graph changes', async () => {
    const firstGraphRequest = createDeferred<typeof sampleGraph>()
    const secondGraphRequest = createDeferred<typeof largeGraph>()
    getGraphBySlugMock.mockImplementation((slug: string) => (
      slug === largeGraph.slug ? secondGraphRequest.promise : firstGraphRequest.promise
    ))

    render(<DemoPage />)

    const selector = await screen.findByRole('combobox', { name: 'Database graph' })
    fireEvent.change(selector, { target: { value: largeGraph.slug } })

    await act(async () => {
      secondGraphRequest.resolve(largeGraph)
    })
    expect(await screen.findByRole('heading', { level: 2, name: largeGraph.title })).toBeInTheDocument()

    await act(async () => {
      firstGraphRequest.resolve(sampleGraph)
    })
    expect(screen.getByRole('heading', { level: 2, name: largeGraph.title })).toBeInTheDocument()
  })

  it('shows a dedicated empty state when the database catalog has no graphs', async () => {
    getGraphCatalogMock.mockResolvedValue([])

    render(<DemoPage />)

    expect(await screen.findByTestId('demo-empty-state')).toBeInTheDocument()
    expect(screen.getByTestId('demo-empty-state')).toHaveTextContent('No database graphs')
    expect(screen.getByRole('combobox', { name: 'Database graph' })).toBeDisabled()
    expect(getGraphBySlugMock).not.toHaveBeenCalled()
  })

  it('reports catalog failures and still allows switching to the fixture', async () => {
    getGraphCatalogMock.mockRejectedValue(new Error('Catalog request failed'))
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    expect(await screen.findByTestId('demo-error-state')).toHaveTextContent('Unable to load graph catalog')
    expect(screen.getByTestId('graph-overview-panel')).toBeInTheDocument()
    expect(getGraphBySlugMock).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Fixture' }))

    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith('sample-medium', 'fixture')
    })
    expect(await screen.findByTestId('insights-graph-canvas')).toBeInTheDocument()
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
    expect(screen.getByRole('combobox', { name: 'Database graph' })).toHaveValue('sample-medium')

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

  it('resets with the selected optional stress graphs before reloading the catalog', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm')
    getGraphBySlugMock.mockResolvedValue(sampleGraph)
    resetDatabaseMock.mockResolvedValue(undefined)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Reset database' }))
    const dialog = screen.getByRole('dialog', { name: 'Reset database' })

    expect(dialog).toHaveTextContent('The standard example graphs are always installed.')
    expect(within(dialog).getByRole('group', { name: '1K stress graphs' })).toBeInTheDocument()
    expect(within(dialog).getByRole('group', { name: '10K stress graphs' })).toBeInTheDocument()
    expect(within(dialog).getByRole('group', { name: '100K stress graphs' })).toBeInTheDocument()

    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Balanced tree \(1,000 nodes\)/ }))
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Deep chain \(10,000 nodes\)/ }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reset and rebuild database' }))

    await waitFor(() => {
      expect(resetDatabaseMock).toHaveBeenCalledWith([
        'stress-balanced-1k',
        'stress-deep-10k',
      ])
      expect(getGraphBySlugMock).toHaveBeenCalledTimes(2)
    })
    expect(confirmSpy).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog', { name: 'Reset database' })).not.toBeInTheDocument()
  })

  it('returns to the first standard graph even when the selected stress graph is reinstalled', async () => {
    getGraphCatalogMock
      .mockResolvedValueOnce(graphCatalogWithStressGraph)
      .mockResolvedValueOnce(graphCatalogWithStressGraph)
    getGraphBySlugMock.mockImplementation((slug: string) => (
      Promise.resolve(slug === balancedStressGraph.slug ? balancedStressGraph : sampleGraph)
    ))
    resetDatabaseMock.mockResolvedValue(undefined)

    render(<DemoPage />)

    await screen.findByTestId('insights-graph-canvas')
    fireEvent.change(screen.getByRole('combobox', { name: 'Database graph' }), {
      target: { value: balancedStressGraph.slug },
    })
    await screen.findByRole('heading', { level: 2, name: balancedStressGraph.title })

    fireEvent.click(screen.getByRole('button', { name: 'Reset database' }))
    const dialog = screen.getByRole('dialog', { name: 'Reset database' })
    expect(within(dialog).getByRole('checkbox', { name: /Balanced tree \(1,000 nodes\)/ }))
      .toBeChecked()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reset and rebuild database' }))

    await waitFor(() => {
      expect(getGraphCatalogMock).toHaveBeenCalledTimes(2)
      expect(screen.getByRole('combobox', { name: 'Database graph' })).toHaveValue(sampleGraph.slug)
      expect(getGraphBySlugMock).toHaveBeenLastCalledWith(sampleGraph.slug, 'database')
    })
    expect(resetDatabaseMock).toHaveBeenCalledWith(['stress-balanced-1k'])
  })

  it('ignores a pre-reset graph response after returning to the first standard graph', async () => {
    const staleStressGraphRequest = createDeferred<typeof balancedStressGraph>()
    getGraphCatalogMock.mockResolvedValue(graphCatalogWithStressGraph)
    getGraphBySlugMock.mockImplementation((slug: string) => (
      slug === balancedStressGraph.slug
        ? staleStressGraphRequest.promise
        : Promise.resolve(sampleGraph)
    ))
    resetDatabaseMock.mockResolvedValue(undefined)

    render(<DemoPage />)

    await screen.findByTestId('insights-graph-canvas')
    fireEvent.change(screen.getByRole('combobox', { name: 'Database graph' }), {
      target: { value: balancedStressGraph.slug },
    })
    await waitFor(() => {
      expect(getGraphBySlugMock).toHaveBeenCalledWith(balancedStressGraph.slug, 'database')
    })

    fireEvent.click(screen.getByRole('button', { name: 'Reset database' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', {
      name: 'Reset and rebuild database',
    }))

    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Database graph' })).toHaveValue(sampleGraph.slug)
      expect(getGraphBySlugMock).toHaveBeenLastCalledWith(sampleGraph.slug, 'database')
    })

    await act(async () => {
      staleStressGraphRequest.resolve(balancedStressGraph)
    })

    expect(screen.getByRole('heading', { level: 2, name: sampleGraph.title })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 2, name: balancedStressGraph.title }))
      .not.toBeInTheDocument()
  })

  it('does not reset when the reset dialog is cancelled', async () => {
    getGraphBySlugMock.mockResolvedValue(sampleGraph)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Reset database' }))
    const dialog = screen.getByRole('dialog', { name: 'Reset database' })
    fireEvent.click(within(dialog).getByRole('checkbox', { name: /Wide star \(1,000 nodes\)/ }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(resetDatabaseMock).not.toHaveBeenCalled()
    expect(getGraphBySlugMock).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('dialog', { name: 'Reset database' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Reset database' }))
    expect(within(screen.getByRole('dialog')).getByRole('checkbox', {
      name: /Wide star \(1,000 nodes\)/,
    })).not.toBeChecked()
  })

  it('keeps the current graph and attempted choices when the reset request fails', async () => {
    getGraphBySlugMock.mockImplementation((slug: string) => (
      Promise.resolve(slug === largeGraph.slug ? largeGraph : sampleGraph)
    ))
    resetDatabaseMock.mockRejectedValue(new Error('Reset failed'))

    render(<DemoPage />)

    await screen.findByTestId('insights-graph-canvas')
    fireEvent.change(screen.getByRole('combobox', { name: 'Database graph' }), {
      target: { value: largeGraph.slug },
    })
    await screen.findByRole('heading', { level: 2, name: largeGraph.title })

    fireEvent.click(screen.getByRole('button', { name: 'Reset database' }))
    const dialog = screen.getByRole('dialog', { name: 'Reset database' })
    const selectedOption = within(dialog).getByRole('checkbox', { name: /Wide star \(10,000 nodes\)/ })
    fireEvent.click(selectedOption)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reset and rebuild database' }))

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'The database reset failed or could not be confirmed. The current view has been retained.',
    )
    expect(selectedOption).toBeChecked()
    expect(screen.getByRole('combobox', { name: 'Database graph' })).toHaveValue(largeGraph.slug)
    expect(screen.getByRole('heading', { level: 2, name: largeGraph.title })).toBeInTheDocument()
    expect(getGraphCatalogMock).toHaveBeenCalledTimes(1)
  })

  it('shows a catalog error after a successful reset and selects the first graph on retry', async () => {
    getGraphCatalogMock
      .mockResolvedValueOnce(graphCatalog)
      .mockRejectedValueOnce(new Error('Catalog refresh failed'))
      .mockResolvedValueOnce(graphCatalog)
    getGraphBySlugMock.mockResolvedValue(sampleGraph)
    resetDatabaseMock.mockResolvedValue(undefined)

    render(<DemoPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Reset database' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', {
      name: 'Reset and rebuild database',
    }))

    expect(await screen.findByTestId('demo-error-state')).toHaveTextContent('Unable to load graph catalog')
    expect(screen.queryByRole('dialog', { name: 'Reset database' })).not.toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Database graph' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Database graph' })).toHaveValue(sampleGraph.slug)
      expect(getGraphBySlugMock).toHaveBeenLastCalledWith(sampleGraph.slug, 'database')
    })
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
