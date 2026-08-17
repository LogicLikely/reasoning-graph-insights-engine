import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { GraphFixture } from '../../fixtures/sampleGraph'
import { InsightsLabDialog } from './InsightsLabDialog'

const serviceMocks = vi.hoisted(() => ({
  getBoundedNodeCounterSet: vi.fn(),
  getEvidenceImpactRanking: vi.fn(),
  getLeastRobustNode: vi.fn(),
  getNodeCounterSet: vi.fn(),
  getNodeRobustnessRanking: vi.fn(),
  updateNode: vi.fn(),
  getPerformanceRuns: vi.fn(),
}))

vi.mock('../../services/graphService', () => ({
  getBoundedNodeCounterSet: serviceMocks.getBoundedNodeCounterSet,
  getEvidenceImpactRanking: serviceMocks.getEvidenceImpactRanking,
  getLeastRobustNode: serviceMocks.getLeastRobustNode,
  getNodeCounterSet: serviceMocks.getNodeCounterSet,
  getNodeRobustnessRanking: serviceMocks.getNodeRobustnessRanking,
  updateNode: serviceMocks.updateNode,
}))

vi.mock('../../services/performanceRuns', () => ({
  getPerformanceRuns: serviceMocks.getPerformanceRuns,
}))

const graph: GraphFixture = {
  slug: 'lab-graph',
  title: 'Lab graph',
  description: 'A graph for Lab tests.',
  nodes: [
    {
      id: 'node-002',
      kind: 'root',
      title: 'Selected root',
      bodyText: '',
      priorOdds: 1,
      posteriorOdds: 1,
    },
    {
      id: 'node-100',
      kind: 'evidence',
      title: 'Highest node',
      bodyText: '',
      priorOdds: 3.25,
      posteriorOdds: 3.25,
    },
    {
      id: 'node-020',
      kind: 'claim',
      title: 'Middle node',
      bodyText: '',
      priorOdds: 2,
      posteriorOdds: 2,
    },
  ],
  edges: [],
}

function emptyReport() {
  return { schemaVersion: 1, runs: [] }
}

function operationCard(name: RegExp) {
  return screen.getByRole('heading', { name }).closest('article') as HTMLElement
}

describe('InsightsLabDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    serviceMocks.getPerformanceRuns.mockResolvedValue(emptyReport())
    serviceMocks.getNodeCounterSet.mockResolvedValue(['node-100'])
    serviceMocks.getBoundedNodeCounterSet.mockResolvedValue({ runNumber: 1 })
    serviceMocks.getEvidenceImpactRanking.mockResolvedValue({ supporting: [], counter: [] })
    serviceMocks.getLeastRobustNode.mockResolvedValue({ nodeId: 'node-100', robustness: 0.2 })
    serviceMocks.getNodeRobustnessRanking.mockResolvedValue([])
    serviceMocks.updateNode.mockResolvedValue(undefined)
  })

  it('loads newest-first history and presents notProven as a completed qualified result', async () => {
    serviceMocks.getPerformanceRuns.mockResolvedValue({
      schemaVersion: 1,
      runs: [
        {
          runNumber: 2,
          startedAtUtc: '2026-08-16T18:42:31.123Z',
          algorithm: {
            name: 'minimal-counter-set',
            implementation: 'bounded-brute-force',
          },
          graph: { slug: 'lab-graph' },
          timing: { operationElapsedMilliseconds: 856.1 },
          outcome: { status: 'notProven', resultCount: 2 },
          details: {
            proofStatus: 'notProven',
            returnedNodeIds: ['node-100', 'node-020'],
            returnedNodeIdsTruncated: false,
          },
        },
        {
          runNumber: 8,
          startedAtUtc: '2026-08-17T18:42:31.123Z',
          algorithm: { name: 'robustness-ranking', implementation: 'current' },
          graph: { slug: 'lab-graph' },
          timing: { operationElapsedMilliseconds: 42 },
          outcome: { status: 'completed', resultCount: 3 },
          details: { rankingPreview: [{ nodeId: 'node-100', robustness: 0.2 }] },
        },
      ],
    })

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
        selectedNodeId="node-002"
      />,
    )

    fireEvent.click(screen.getByRole('tab', { name: /History/ }))
    const rows = await screen.findAllByRole('row')
    expect(within(rows[1]).getByRole('button', { name: 'View run 8' })).toBeInTheDocument()
    expect(within(rows[2]).getByRole('button', { name: 'View run 2' })).toBeInTheDocument()

    fireEvent.click(within(rows[2]).getByRole('button', { name: 'View run 2' }))
    const report = screen.getByRole('article', { name: 'Report for run 2' })
    expect(within(report).getAllByText('Completed').length).toBeGreaterThan(0)
    expect(within(report).getAllByText('Not proven').length).toBeGreaterThan(0)
    expect(within(report).getByText('Returned node IDs')).toBeInTheDocument()
    expect(within(report).getAllByText('node-100').length).toBeGreaterThan(0)
  })

  it('uses a fresh watermark, runs against the selected node, and opens the matching report', async () => {
    const completedRun = {
      runNumber: 12,
      startedAtUtc: '2026-08-17T18:42:31.123Z',
      algorithm: { name: 'minimal-counter-set', implementation: 'greedy' },
      graph: { slug: 'lab-graph' },
      invocation: { dataSource: 'database', targetNodeId: 'node-002' },
      timing: { operationElapsedMilliseconds: 18.4 },
      outcome: { status: 'completed', resultCount: 1 },
      details: { returnedNodeIds: ['node-100'] },
    }
    serviceMocks.getPerformanceRuns
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce({ schemaVersion: 1, runs: [{ ...completedRun, runNumber: 11 }] })
      .mockResolvedValueOnce({
        schemaVersion: 1,
        runs: [
          {
            ...completedRun,
            runNumber: 13,
            invocation: { dataSource: 'fixture', targetNodeId: 'node-002' },
          },
          completedRun,
        ],
      })

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
        selectedNodeId="node-002"
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(1))

    fireEvent.click(within(operationCard(/Minimal counter set/)).getByRole('button', {
      name: 'Run Minimal counter set',
    }))

    await screen.findByRole('article', { name: 'Report for run 12' })
    expect(serviceMocks.getNodeCounterSet).toHaveBeenCalledWith(
      'lab-graph',
      'node-002',
      'database',
      expect.any(AbortSignal),
    )
    expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(3)
    expect(screen.getByRole('tab', { name: /History/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: /History/ })).toHaveFocus()
  })

  it('runs the database leaf update against the ordinal-highest node without changing prior odds', async () => {
    const onGraphUpdated = vi.fn()
    const leafRun = {
      runNumber: 1,
      algorithm: { name: 'leaf-update', implementation: 'current' },
      graph: { slug: 'lab-graph' },
      invocation: { dataSource: 'database', changedNodeId: 'node-100' },
      timing: { operationElapsedMilliseconds: 2 },
      outcome: { status: 'completed' },
      details: {},
    }
    serviceMocks.getPerformanceRuns
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce({ schemaVersion: 1, runs: [leafRun] })

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
        onGraphUpdated={onGraphUpdated}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(1))
    fireEvent.click(within(operationCard(/^Leaf update$/)).getByRole('button', {
      name: 'Run Leaf update',
    }))

    await screen.findByRole('article', { name: 'Report for run 1' })
    expect(serviceMocks.updateNode).toHaveBeenCalledWith(
      'lab-graph',
      'node-100',
      { priorOdds: 3.25 },
    )
    expect(onGraphUpdated).toHaveBeenCalledOnce()
  })

  it.each([
    {
      title: 'Bounded minimal counter set',
      service: 'getBoundedNodeCounterSet' as const,
      algorithm: 'minimal-counter-set',
      implementation: 'bounded-brute-force',
      baseArguments: ['lab-graph', 'node-002', 'database'],
      cancellable: true,
    },
    {
      title: 'Evidence impact ranking',
      service: 'getEvidenceImpactRanking' as const,
      algorithm: 'evidence-impact-ranking',
      implementation: 'current',
      baseArguments: ['lab-graph', 'node-002', 'database'],
      cancellable: false,
    },
    {
      title: 'Least robust node',
      service: 'getLeastRobustNode' as const,
      algorithm: 'least-robust-node',
      implementation: 'current',
      baseArguments: ['lab-graph', 'database'],
      cancellable: true,
    },
    {
      title: 'Robustness ranking',
      service: 'getNodeRobustnessRanking' as const,
      algorithm: 'robustness-ranking',
      implementation: 'current',
      baseArguments: ['lab-graph', 'database'],
      cancellable: true,
    },
  ])('launches $title and opens its report', async ({
    title,
    service,
    algorithm,
    implementation,
    baseArguments,
    cancellable,
  }) => {
    const storedRun = {
      runNumber: 1,
      algorithm: { name: algorithm, implementation },
      graph: { slug: 'lab-graph' },
      invocation: {
        dataSource: 'database',
        ...(baseArguments.length === 3 ? { targetNodeId: 'node-002' } : {}),
      },
      outcome: { status: 'completed' },
      details: {},
    }
    serviceMocks.getPerformanceRuns
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce({ schemaVersion: 1, runs: [storedRun] })

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
        selectedNodeId="node-002"
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())

    fireEvent.click(within(operationCard(new RegExp(`^${title}$`))).getByRole('button', {
      name: `Run ${title}`,
    }))

    await screen.findByRole('article', { name: 'Report for run 1' })
    const call = serviceMocks[service].mock.calls[0]
    expect(call.slice(0, baseArguments.length)).toEqual(baseArguments)
    if (cancellable) expect(call[baseArguments.length]).toBeInstanceOf(AbortSignal)
    else expect(call).toEqual(baseArguments)
  })

  it('offers best-effort cancellation only for cancellable operations and keeps the dialog open', async () => {
    let receivedSignal: AbortSignal | undefined
    serviceMocks.getNodeCounterSet.mockImplementation((...args: unknown[]) => {
      receivedSignal = args[3] as AbortSignal
      return new Promise((_, reject) => {
        receivedSignal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
      })
    })
    const cancelledRun = {
      runNumber: 1,
      algorithm: { name: 'minimal-counter-set', implementation: 'greedy' },
      graph: { slug: 'lab-graph' },
      invocation: { dataSource: 'database', targetNodeId: 'node-002' },
      outcome: { status: 'cancelled' },
      details: {},
    }
    serviceMocks.getPerformanceRuns
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce({ schemaVersion: 1, runs: [cancelledRun] })

    const onClose = vi.fn()
    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={onClose}
        selectedNodeId="node-002"
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(1))
    fireEvent.click(within(operationCard(/Minimal counter set/)).getByRole('button', {
      name: 'Run Minimal counter set',
    }))

    const cancel = await screen.findByRole('button', { name: 'Cancel run' })
    expect(screen.getByText('Running…').closest('[role="status"]')).toHaveFocus()
    expect(screen.getByRole('button', { name: 'Close Insights Lab' })).toBeDisabled()
    fireEvent.click(cancel)

    expect(await screen.findByText('Run cancelled. Its report is shown below.')).toBeInTheDocument()
    expect(screen.getByRole('article', { name: 'Report for run 1' })).toBeInTheDocument()
    expect(receivedSignal?.aborted).toBe(true)
    expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(4)
    expect(onClose).not.toHaveBeenCalled()
  })

  it('skips leaf updates in fixture mode', async () => {
    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="fixture"
        isOpen
        onClose={vi.fn()}
        selectedNodeId="node-002"
      />,
    )

    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())

    expect(within(operationCard(/^Leaf update$/)).getByRole('button', {
      name: 'Run Leaf update',
    })).toBeDisabled()
    expect(screen.getByText('Database graphs only; fixture updates are skipped.')).toBeInTheDocument()
  })

  it('disables leaf update when the active graph has no nodes', async () => {
    render(
      <InsightsLabDialog
        graph={{ ...graph, nodes: [] }}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())

    expect(within(operationCard(/^Leaf update$/)).getByRole('button', {
      name: 'Run Leaf update',
    })).toBeDisabled()
    expect(screen.getByText('This graph has no node to update.')).toBeInTheDocument()
  })

  it('isolates the page behind the modal and restores focus when it closes', async () => {
    const onClose = vi.fn()
    const view = render(<button type="button">Open Lab</button>)
    const opener = screen.getByRole('button', { name: 'Open Lab' })
    opener.focus()

    view.rerender(
      <>
        <button type="button">Open Lab</button>
        <InsightsLabDialog
          graph={graph}
          graphDataSource="database"
          isOpen
          onClose={onClose}
          selectedNodeId="node-002"
        />
      </>,
    )

    expect(opener).toHaveAttribute('inert')
    expect(opener).toHaveAttribute('aria-hidden', 'true')
    expect(screen.getByRole('button', { name: 'Close Insights Lab' })).toHaveFocus()

    const runTab = screen.getByRole('tab', { name: 'Run' })
    runTab.focus()
    fireEvent.keyDown(runTab, { key: 'ArrowRight' })
    expect(screen.getByRole('tab', { name: /History/ })).toHaveFocus()
    expect(screen.getByRole('tab', { name: /History/ })).toHaveAttribute('aria-selected', 'true')

    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(onClose).toHaveBeenCalledOnce()
    view.rerender(
      <>
        <button type="button">Open Lab</button>
        <InsightsLabDialog
          graph={graph}
          graphDataSource="database"
          isOpen={false}
          onClose={onClose}
          selectedNodeId="node-002"
        />
      </>,
    )

    expect(opener).not.toHaveAttribute('inert')
    expect(opener).not.toHaveAttribute('aria-hidden')
    expect(opener).toHaveFocus()
  })
})
