import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { GraphFixture } from '../../fixtures/sampleGraph'
import { STRESS_GRAPH_OPTIONS } from '../../services/stressGraphs'
import { InsightsLabDialog } from './InsightsLabDialog'

const serviceMocks = vi.hoisted(() => ({
  getBoundedNodeCounterSet: vi.fn(),
  getEvidenceImpactRanking: vi.fn(),
  getGraphBySlug: vi.fn(),
  getGraphCatalog: vi.fn(),
  getLeastRobustNode: vi.fn(),
  getNodeCounterSet: vi.fn(),
  getNodeRobustnessRanking: vi.fn(),
  updateNode: vi.fn(),
  createBenchmarkSet: vi.fn(),
  getPerformanceRuns: vi.fn(),
}))

vi.mock('../../services/graphService', () => ({
  getBoundedNodeCounterSet: serviceMocks.getBoundedNodeCounterSet,
  getEvidenceImpactRanking: serviceMocks.getEvidenceImpactRanking,
  getGraphBySlug: serviceMocks.getGraphBySlug,
  getGraphCatalog: serviceMocks.getGraphCatalog,
  getLeastRobustNode: serviceMocks.getLeastRobustNode,
  getNodeCounterSet: serviceMocks.getNodeCounterSet,
  getNodeRobustnessRanking: serviceMocks.getNodeRobustnessRanking,
  updateNode: serviceMocks.updateNode,
}))

vi.mock('../../services/performanceRuns', () => ({
  createBenchmarkSet: serviceMocks.createBenchmarkSet,
  getPerformanceRuns: serviceMocks.getPerformanceRuns,
}))

const benchmarkSet = {
  id: 'benchmark-01',
  name: 'LL-699 baseline',
  createdAtUtc: '2026-08-17T12:00:00Z',
}

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

function report(runs: unknown[] = [], benchmarkSets = [benchmarkSet]) {
  return { schemaVersion: 2, benchmarkSets, runs }
}

function emptyReport() {
  return report()
}

function operationCard(name: RegExp) {
  return screen.getByRole('heading', { name }).closest('article') as HTMLElement
}

describe('InsightsLabDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.localStorage.clear()
    serviceMocks.getPerformanceRuns.mockResolvedValue(emptyReport())
    serviceMocks.getNodeCounterSet.mockResolvedValue(['node-100'])
    serviceMocks.getBoundedNodeCounterSet.mockResolvedValue({
      counterNodeIds: [],
      proofStatus: 'proven',
      runNumber: 1,
      status: 'completed',
      stopReason: 'completed',
      timeBudgetMilliseconds: 120_000,
    })
    serviceMocks.getEvidenceImpactRanking.mockResolvedValue({ supporting: [], counter: [] })
    serviceMocks.getGraphBySlug.mockResolvedValue(graph)
    serviceMocks.getGraphCatalog.mockResolvedValue([])
    serviceMocks.getLeastRobustNode.mockResolvedValue({ nodeId: 'node-100', robustness: 0.2 })
    serviceMocks.getNodeRobustnessRanking.mockResolvedValue([])
    serviceMocks.updateNode.mockResolvedValue(undefined)
    serviceMocks.createBenchmarkSet.mockResolvedValue(benchmarkSet)
  })

  it('requires a benchmark set and selects a newly created backend identity', async () => {
    serviceMocks.getPerformanceRuns.mockResolvedValue(report([], []))
    const created = {
      id: 'backend-generated-id',
      name: 'Bayesian rework',
      createdAtUtc: '2026-08-17T15:00:00Z',
    }
    serviceMocks.createBenchmarkSet.mockResolvedValue(created)

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    expect(screen.getByRole('tab', { name: 'Run' })).toHaveAttribute('aria-selected', 'true')
    const runButton = within(operationCard(/Minimal counter set/)).getByRole('button', {
      name: 'Run Minimal counter set',
    })
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    expect(runButton).toBeDisabled()
    expect(screen.getByText('Select or create a benchmark set before running an algorithm.')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('New benchmark set'), {
      target: { value: '  Bayesian rework  ' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create and select' }))

    await waitFor(() => expect(serviceMocks.createBenchmarkSet).toHaveBeenCalledWith('Bayesian rework'))
    expect(screen.getByLabelText('Benchmark set')).toHaveValue(created.id)
    await waitFor(() => {
      expect(window.localStorage.getItem('insights-lab-benchmark-set-id')).toBe(created.id)
    })
    expect(runButton).toBeEnabled()
  })

  it('blocks new runs while keeping recorded History and Trends available', async () => {
    const alternateBenchmarkSet = {
      id: 'benchmark-02',
      name: 'LL-699 follow-up',
      createdAtUtc: '2026-08-18T12:00:00Z',
    }
    const recordedRun = {
      runNumber: 7,
      benchmarkSetId: benchmarkSet.id,
      startedAtUtc: '2026-08-17T18:42:31.123Z',
      algorithm: { name: 'minimal-counter-set', implementation: 'greedy' },
      graph: { slug: 'lab-graph' },
      invocation: { dataSource: 'database', targetNodeId: 'node-002' },
      timing: { operationElapsedMilliseconds: 18.4 },
      outcome: { status: 'completed', resultCount: 1 },
      details: { returnedNodeIds: ['node-100'] },
    }
    serviceMocks.getPerformanceRuns.mockResolvedValue(
      report([recordedRun], [benchmarkSet, alternateBenchmarkSet]),
    )
    window.localStorage.setItem('insights-lab-benchmark-set-id', benchmarkSet.id)
    const alertSpy = vi.spyOn(window, 'alert').mockImplementation(() => undefined)

    try {
      render(
        <InsightsLabDialog
          allowNewRuns={false}
          graph={graph}
          graphDataSource="database"
          installedStressGraphIds={['stress-balanced-100']}
          isOpen
          onClose={vi.fn()}
        />,
      )

      await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
      expect(screen.getByRole('tab', { name: 'Trends' })).toHaveAttribute('aria-selected', 'true')
      expect(screen.getByRole('heading', { name: 'Historical trends' })).toBeInTheDocument()

      fireEvent.click(screen.getByRole('tab', { name: 'Run' }))
      const benchmarkSelect = screen.getByLabelText('Benchmark set')
      expect(benchmarkSelect).toHaveValue('')
      for (const runButton of screen.getAllByRole('button', { name: /^Run / })) {
        expect(runButton).toBeDisabled()
      }

      fireEvent.change(benchmarkSelect, { target: { value: alternateBenchmarkSet.id } })
      expect(benchmarkSelect).toHaveValue('')
      expect(alertSpy).toHaveBeenLastCalledWith(
        'New Insights Lab runs are disabled in this demo. You can still review saved runs in History and Trends.',
      )

      fireEvent.change(screen.getByLabelText('New benchmark set'), {
        target: { value: 'Attempted benchmark' },
      })
      fireEvent.click(screen.getByRole('button', { name: 'Create and select' }))
      expect(alertSpy).toHaveBeenCalledTimes(2)
      expect(alertSpy).toHaveBeenLastCalledWith(
        'New Insights Lab runs are disabled in this demo. You can still review saved runs in History and Trends.',
      )

      for (const runButton of screen.getAllByRole('button', { name: /^Run / })) {
        fireEvent.click(runButton)
      }
      expect(serviceMocks.createBenchmarkSet).not.toHaveBeenCalled()
      expect(serviceMocks.getNodeCounterSet).not.toHaveBeenCalled()
      expect(serviceMocks.getBoundedNodeCounterSet).not.toHaveBeenCalled()
      expect(serviceMocks.getEvidenceImpactRanking).not.toHaveBeenCalled()
      expect(serviceMocks.getLeastRobustNode).not.toHaveBeenCalled()
      expect(serviceMocks.getNodeRobustnessRanking).not.toHaveBeenCalled()
      expect(serviceMocks.updateNode).not.toHaveBeenCalled()
      expect(serviceMocks.getGraphCatalog).not.toHaveBeenCalled()
      expect(serviceMocks.getGraphBySlug).not.toHaveBeenCalled()

      fireEvent.click(screen.getByRole('tab', { name: /History/ }))
      expect(await screen.findByRole('button', { name: 'View run 7' })).toBeInTheDocument()

      fireEvent.click(screen.getByRole('tab', { name: 'Trends' }))
      expect(screen.getByRole('heading', { name: 'Historical trends' })).toBeInTheDocument()
    } finally {
      alertSpy.mockRestore()
    }
  })

  it('does not enable new runs when disabled mode suppresses automatic benchmark selection', async () => {
    render(
      <InsightsLabDialog
        allowNewRuns={false}
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    expect(screen.getByRole('tab', { name: 'Trends' })).toHaveAttribute('aria-selected', 'true')
    fireEvent.click(screen.getByRole('tab', { name: 'Run' }))
    expect(screen.getByLabelText('Benchmark set')).toHaveValue('')
    for (const runButton of screen.getAllByRole('button', { name: /^Run / })) {
      expect(runButton).toBeDisabled()
    }
  })

  it('uses and explains the canonical root for target-based stress runs', async () => {
    const stressGraph = {
      ...graph,
      slug: 'stress-balanced-1k',
      nodes: [
        ...graph.nodes,
        {
          id: 'n-00000',
          kind: 'root' as const,
          title: 'Stress root',
          bodyText: '',
          priorOdds: 1,
          posteriorOdds: 1,
        },
      ],
    }
    const completedRun = {
      runNumber: 1,
      benchmarkSetId: benchmarkSet.id,
      algorithm: { name: 'minimal-counter-set', implementation: 'greedy' },
      graph: { slug: stressGraph.slug },
      invocation: { dataSource: 'database', targetNodeId: 'n-00000' },
      outcome: { status: 'completed' },
      details: {},
    }
    serviceMocks.getPerformanceRuns
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(report([completedRun]))

    render(
      <InsightsLabDialog
        graph={stressGraph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    expect(await screen.findByText('Root (n-00000)')).toBeInTheDocument()
    expect(screen.getByText(/greedy counter-set search for the graph root/i)).toBeInTheDocument()
    const runButton = within(operationCard(/Minimal counter set/)).getByRole('button', {
      name: 'Run Minimal counter set',
    })
    await waitFor(() => expect(runButton).toBeEnabled())
    fireEvent.click(runButton)

    await screen.findByRole('article', { name: 'Report for run 1' })
    expect(serviceMocks.getNodeCounterSet).toHaveBeenCalledWith(
      stressGraph.slug,
      'n-00000',
      'database',
      expect.any(AbortSignal),
      benchmarkSet.id,
    )
  })

  it('shows the active graph beside its target only on the Run tab', async () => {
    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    const graphIdentity = screen.getByText('Lab graph · lab-graph')
    const context = graphIdentity.closest('.insights-lab-dialog__context') as HTMLElement
    expect(within(context).getByText('Active database graph')).toBeInTheDocument()
    expect(within(context).getByText('Canonical algorithm target')).toBeInTheDocument()
    expect(within(context).getByText('Root (node-002)')).toBeInTheDocument()
    expect(within(screen.getByRole('heading', { name: 'Insights Lab' }).closest('header') as HTMLElement)
      .queryByText('Lab graph · lab-graph')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: /History/ }))
    expect(screen.queryByText('Lab graph · lab-graph')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'Trends' }))
    expect(screen.queryByText('Lab graph · lab-graph')).not.toBeInTheDocument()
  })

  it('loads newest-first history and explains a time-limited partial result', async () => {
    serviceMocks.getPerformanceRuns.mockResolvedValue({
      schemaVersion: 2,
      benchmarkSets: [benchmarkSet],
      runs: [
        {
          runNumber: 2,
          benchmarkSetId: benchmarkSet.id,
          startedAtUtc: '2026-08-16T18:42:31.123Z',
          algorithm: {
            name: 'minimal-counter-set',
            implementation: 'time-bounded-exhaustive',
          },
          graph: { slug: 'lab-graph' },
          invocation: { parameters: { timeBudgetMilliseconds: 120_000 } },
          timing: { operationElapsedMilliseconds: 120_856.1 },
          outcome: { status: 'timedOut', resultCount: 2, proofStatus: 'notProven' },
          details: {
            proofStatus: 'notProven',
            stopReason: 'timeBudget',
            thresholdReached: false,
            returnedNodeIds: ['node-100', 'node-020'],
            returnedNodeIdsTruncated: false,
            totalCandidateCount: 33,
            subsetEvaluations: 4_194_304,
            largestCardinalityFullyExhausted: 5,
            activeCardinality: 6,
            subsetEvaluationsAtActiveCardinality: 250_000,
            totalSubsetsAtActiveCardinality: '1107568',
            timeoutStage: 'search',
          },
        },
        {
          runNumber: 8,
          benchmarkSetId: benchmarkSet.id,
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
      />,
    )

    fireEvent.click(screen.getByRole('tab', { name: /History/ }))
    const historyRegion = await screen.findByRole('region', { name: 'Performance run history' })
    expect(historyRegion).toHaveAttribute('tabindex', '0')
    expect(screen.queryByRole('article', { name: /Report for run/ })).not.toBeInTheDocument()
    const rows = await screen.findAllByRole('row')
    expect(within(rows[1]).getByRole('button', { name: 'View run 8' })).toBeInTheDocument()
    expect(within(rows[2]).getByRole('button', { name: 'View run 2' })).toBeInTheDocument()

    fireEvent.click(within(rows[2]).getByRole('button', { name: 'View run 2' }))
    expect(screen.queryByRole('region', { name: 'Performance run history' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Back to all runs' })).toHaveFocus()
    const report = screen.getByRole('article', { name: 'Report for run 2' })
    expect(within(report).getAllByText('Timed out').length).toBeGreaterThan(0)
    expect(within(report).getAllByText('Not proven').length).toBeGreaterThan(0)
    expect(within(report).getByText(benchmarkSet.name)).toBeInTheDocument()
    expect(within(report).getByText(benchmarkSet.id)).toBeInTheDocument()
    expect(within(report).getByText('Best set found before time limit — threshold not reached')).toBeInTheDocument()
    expect(within(report).queryByText('Returned node IDs')).not.toBeInTheDocument()
    expect(within(report).getAllByText('node-100').length).toBeGreaterThan(0)
    const timeLimit = within(report).getByRole('region', { name: 'Exhaustive search time limit' })
    expect(within(timeLimit).getByText('Search stopped at the 2-minute budget')).toBeInTheDocument()
    expect(within(timeLimit).getByText('All sets through size 5')).toBeInTheDocument()
    expect(within(timeLimit).getByText('Sets of size 6')).toBeInTheDocument()
    expect(within(timeLimit).getByText('250,000 of 1,107,568 sets evaluated')).toBeInTheDocument()
    expect(within(timeLimit).getByText(/expected partial benchmark result, not a failed request/i)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Back to all runs' }))
    expect(await screen.findByRole('region', { name: 'Performance run history' })).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'Report for run 2' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'View run 2' })).toHaveFocus()
  })

  it('does not imply that a candidate set was evaluated when preparation uses the time budget', async () => {
    serviceMocks.getPerformanceRuns.mockResolvedValue(report([{
      runNumber: 3,
      benchmarkSetId: benchmarkSet.id,
      algorithm: {
        name: 'minimal-counter-set',
        implementation: 'time-bounded-exhaustive',
      },
      graph: { slug: 'lab-graph' },
      invocation: { parameters: { timeBudgetMilliseconds: 120_000 } },
      outcome: { status: 'timedOut', resultCount: 0, proofStatus: 'notProven' },
      details: {
        proofStatus: 'notProven',
        stopReason: 'timeBudget',
        timeoutStage: 'preparation',
        thresholdReached: false,
        subsetEvaluations: 0,
        returnedNodeIds: [],
      },
    }]))

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    fireEvent.click(screen.getByRole('tab', { name: /History/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'View run 3' }))

    const reportArticle = screen.getByRole('article', { name: 'Report for run 3' })
    expect(within(reportArticle).getByText('No candidate set evaluated before time limit')).toBeInTheDocument()
    expect(within(reportArticle).queryByText(/Best set found/)).not.toBeInTheDocument()
    expect(within(reportArticle).queryByText('No nodes (empty set)')).not.toBeInTheDocument()
  })

  it('retains the evaluated empty set when the time budget expires after problem preparation', async () => {
    serviceMocks.getPerformanceRuns.mockResolvedValue(report([{
      runNumber: 4,
      benchmarkSetId: benchmarkSet.id,
      algorithm: {
        name: 'minimal-counter-set',
        implementation: 'time-bounded-exhaustive',
      },
      graph: { slug: 'lab-graph' },
      invocation: { parameters: { timeBudgetMilliseconds: 120_000 } },
      outcome: { status: 'timedOut', resultCount: 0, proofStatus: 'notProven' },
      details: {
        proofStatus: 'notProven',
        stopReason: 'timeBudget',
        timeoutStage: 'preparation',
        thresholdReached: false,
        subsetEvaluations: 1,
        largestCardinalityFullyExhausted: 0,
        returnedNodeIds: [],
      },
    }]))

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    fireEvent.click(screen.getByRole('tab', { name: /History/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'View run 4' }))

    const reportArticle = screen.getByRole('article', { name: 'Report for run 4' })
    expect(within(reportArticle).getByText('Best set found before time limit — threshold not reached')).toBeInTheDocument()
    expect(within(reportArticle).getByText('No nodes (empty set)')).toBeInTheDocument()
    expect(within(reportArticle).queryByText('No candidate set evaluated before time limit')).not.toBeInTheDocument()
  })

  it('uses a fresh watermark, runs against the deterministic root, and opens the matching report', async () => {
    const completedRun = {
      runNumber: 12,
      benchmarkSetId: benchmarkSet.id,
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
      .mockResolvedValueOnce(report([{ ...completedRun, runNumber: 11 }]))
      .mockResolvedValueOnce({
        schemaVersion: 2,
        benchmarkSets: [benchmarkSet],
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
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(1))

    const runButton = within(operationCard(/Minimal counter set/)).getByRole('button', {
      name: 'Run Minimal counter set',
    })
    await waitFor(() => expect(runButton).toBeEnabled())
    fireEvent.click(runButton)

    await screen.findByRole('article', { name: 'Report for run 12' })
    expect(serviceMocks.getNodeCounterSet).toHaveBeenCalledWith(
      'lab-graph',
      'node-002',
      'database',
      expect.any(AbortSignal),
      benchmarkSet.id,
    )
    expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(3)
    expect(screen.getByRole('tab', { name: /History/ })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('button', { name: 'Back to all runs' })).toHaveFocus()
    expect(screen.queryByRole('region', { name: 'Performance run history' })).not.toBeInTheDocument()
  })

  it('presents a proven zero-counter result as the empty set', async () => {
    serviceMocks.getPerformanceRuns.mockResolvedValue(report([{
      runNumber: 1,
      benchmarkSetId: benchmarkSet.id,
      algorithm: { name: 'minimal-counter-set', implementation: 'time-bounded-exhaustive' },
      graph: { slug: 'lab-graph' },
      outcome: { status: 'completed', resultCount: 0 },
      details: {
        proofStatus: 'proven',
        thresholdReached: true,
        returnedNodeIds: [],
      },
    }]))

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    fireEvent.click(screen.getByRole('tab', { name: /History/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'View run 1' }))
    const reportArticle = screen.getByRole('article', { name: 'Report for run 1' })
    expect(within(reportArticle).getByText('Returned node IDs')).toBeInTheDocument()
    expect(within(reportArticle).getByText('None (empty set)')).toBeInTheDocument()
  })

  it.each([
    ['Minimal counter set', /most promising counters first/i],
    ['Time-bounded exhaustive search', /every counter-set method must enumerate combinations/i],
    ['Evidence impact ranking', /not whether a piece of evidence is true/i],
    ['Least robust node', /removing each downstream piece.*one at a time/i],
    ['Robustness ranking', /one-at-a-time downstream evidence test/i],
    ['Leaf update', /highest-ID node is treated as a likely leaf/i],
  ])('explains %s in a click-triggered information panel', async (title, expectedCopy) => {
    const onClose = vi.fn()
    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={onClose}
      />,
    )

    const card = operationCard(new RegExp(`^${title}$`))
    await waitFor(() => expect(within(card).getByRole('button', {
      name: `Run ${title}`,
    })).toBeEnabled())
    const infoButton = within(card).getByRole('button', { name: `About ${title}` })
    expect(infoButton).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(infoButton)

    const info = screen.getByRole('region', { name: `About ${title}` })
    expect(infoButton).toHaveAttribute('aria-expanded', 'true')
    expect(within(info).getByText(expectedCopy)).toBeInTheDocument()

    infoButton.focus()
    fireEvent.keyDown(infoButton, { key: 'Escape' })
    expect(screen.queryByRole('region', { name: `About ${title}` })).not.toBeInTheDocument()
    expect(infoButton).toHaveFocus()
    expect(onClose).not.toHaveBeenCalled()
  })

  it('runs the database leaf update against the ordinal-highest node without changing prior odds', async () => {
    const onGraphUpdated = vi.fn()
    const leafRun = {
      runNumber: 1,
      benchmarkSetId: benchmarkSet.id,
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
      .mockResolvedValueOnce(report([leafRun]))

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
    const runButton = within(operationCard(/^Leaf update$/)).getByRole('button', {
      name: 'Run Leaf update',
    })
    await waitFor(() => expect(runButton).toBeEnabled())
    fireEvent.click(runButton)

    await screen.findByRole('article', { name: 'Report for run 1' })
    expect(serviceMocks.updateNode).toHaveBeenCalledWith(
      'lab-graph',
      'node-100',
      { priorOdds: 3.25 },
      benchmarkSet.id,
    )
    expect(onGraphUpdated).toHaveBeenCalledOnce()
  })

  it.each([
    {
      title: 'Time-bounded exhaustive search',
      service: 'getBoundedNodeCounterSet' as const,
      algorithm: 'minimal-counter-set',
      implementation: 'time-bounded-exhaustive',
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
      benchmarkSetId: benchmarkSet.id,
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
      .mockResolvedValueOnce(report([storedRun]))

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())

    const runButton = within(operationCard(new RegExp(`^${title}$`))).getByRole('button', {
      name: `Run ${title}`,
    })
    await waitFor(() => expect(runButton).toBeEnabled())
    fireEvent.click(runButton)

    await screen.findByRole('article', { name: 'Report for run 1' })
    const call = serviceMocks[service].mock.calls[0]
    expect(call.slice(0, baseArguments.length)).toEqual(baseArguments)
    if (cancellable) expect(call[baseArguments.length]).toBeInstanceOf(AbortSignal)
    else expect(call[baseArguments.length]).toBeUndefined()
    expect(call[baseArguments.length + 1]).toBe(benchmarkSet.id)
  })

  it('opens an HTTP-success timeout as a partial report instead of a failed run', async () => {
    const timedOutRun = {
      runNumber: 7,
      benchmarkSetId: benchmarkSet.id,
      algorithm: { name: 'minimal-counter-set', implementation: 'time-bounded-exhaustive' },
      graph: { slug: 'lab-graph' },
      invocation: {
        dataSource: 'database',
        targetNodeId: 'node-002',
        parameters: { timeBudgetMilliseconds: 120_000 },
      },
      outcome: { status: 'timedOut', proofStatus: 'notProven' },
      details: {
        proofStatus: 'notProven',
        stopReason: 'timeBudget',
        largestCardinalityFullyExhausted: 4,
        activeCardinality: 5,
      },
    }
    serviceMocks.getBoundedNodeCounterSet.mockResolvedValueOnce({
      counterNodeIds: null,
      proofStatus: 'notProven',
      runNumber: 7,
      status: 'timedOut',
      stopReason: 'timeBudget',
      timeBudgetMilliseconds: 120_000,
    })
    serviceMocks.getPerformanceRuns
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(report([timedOutRun]))

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())

    fireEvent.click(within(operationCard(/^Time-bounded exhaustive search$/)).getByRole('button', {
      name: 'Run Time-bounded exhaustive search',
    }))

    expect(await screen.findByText(/reached its two-minute compute budget/i)).toBeInTheDocument()
    const reportArticle = screen.getByRole('article', { name: 'Report for run 7' })
    expect(within(reportArticle).getAllByText('Timed out').length).toBeGreaterThan(0)
    expect(screen.queryByText(/did not complete successfully/i)).not.toBeInTheDocument()
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
      benchmarkSetId: benchmarkSet.id,
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
      .mockResolvedValueOnce(report([cancelledRun]))

    const onClose = vi.fn()
    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={onClose}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(1))
    const runButton = within(operationCard(/Minimal counter set/)).getByRole('button', {
      name: 'Run Minimal counter set',
    })
    await waitFor(() => expect(runButton).toBeEnabled())
    fireEvent.click(runButton)

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

  it('runs every operation sequentially for each installed standard stress graph and refreshes history', async () => {
    const sequence: string[] = []
    const standardGraphOptions = STRESS_GRAPH_OPTIONS.filter(
      ({ id }) => !id.startsWith('stress-deep-'),
    )
    let releaseFirstRun!: () => void
    const firstRun = new Promise<void>((resolve) => {
      releaseFirstRun = resolve
    })
    let isFirstRun = true

    serviceMocks.getNodeCounterSet.mockImplementation(async (slug: string) => {
      sequence.push(`${slug}:minimal`)
      if (isFirstRun) {
        isFirstRun = false
        await firstRun
      }
      return []
    })
    serviceMocks.getBoundedNodeCounterSet.mockImplementation(async (slug: string) => {
      sequence.push(`${slug}:bounded`)
      return { runNumber: 1 }
    })
    serviceMocks.getEvidenceImpactRanking.mockImplementation(async (slug: string) => {
      sequence.push(`${slug}:evidence`)
      return { supportingEvidence: [], counterEvidence: [] }
    })
    serviceMocks.getLeastRobustNode.mockImplementation(async (slug: string) => {
      sequence.push(`${slug}:least`)
      return { nodeId: 'n-00000', robustness: 0.2 }
    })
    serviceMocks.getNodeRobustnessRanking.mockImplementation(async (slug: string) => {
      sequence.push(`${slug}:ranking`)
      return []
    })
    serviceMocks.getGraphBySlug.mockImplementation(async (slug: string) => {
      sequence.push(`${slug}:load`)
      const highestNodeId = slug.endsWith('-100k')
        ? 'n-99999'
        : slug.endsWith('-10k')
          ? 'n-09999'
          : slug.endsWith('-1k')
            ? 'n-00999'
            : 'n-00099'
      return {
        ...graph,
        slug,
        nodes: [
          { ...graph.nodes[0], id: 'n-00000' },
          { ...graph.nodes[1], id: highestNodeId, priorOdds: 4.5 },
        ],
      }
    })
    serviceMocks.updateNode.mockImplementation(async (slug: string) => {
      sequence.push(`${slug}:leaf`)
    })
    const onGraphUpdated = vi.fn()

    render(
      <InsightsLabDialog
        graph={{ ...graph, slug: 'stress-balanced-1k' }}
        graphDataSource="database"
        installedStressGraphIds={STRESS_GRAPH_OPTIONS.map(({ id }) => id)}
        isOpen
        onClose={vi.fn()}
        onGraphUpdated={onGraphUpdated}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())

    expect(screen.getByText(/12 installed standard database stress graphs \(72 planned runs\)/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Run standard stress suite' }))
    expect(await screen.findByText(/1 of 72 · Balanced tree \(100 nodes\) · Minimal counter set/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Stop suite' })).toBeInTheDocument()
    releaseFirstRun()

    expect(await screen.findByText('Standard stress suite complete')).toBeInTheDocument()
    expect(screen.getByText(/72 requests completed/)).toBeInTheDocument()
    expect(screen.getByText('Standard stress suite complete').closest('[role="status"]')).toHaveFocus()
    expect(sequence).toEqual(standardGraphOptions.flatMap(({ id }) => [
      `${id}:minimal`,
      `${id}:bounded`,
      `${id}:evidence`,
      `${id}:least`,
      `${id}:ranking`,
      `${id}:load`,
      `${id}:leaf`,
    ]))
    expect(serviceMocks.getNodeCounterSet.mock.calls.map((call) => call.slice(0, 3))).toEqual([
      ...standardGraphOptions.map(({ id }) => [id, 'n-00000', 'database']),
    ])
    expect(serviceMocks.getGraphBySlug.mock.calls).toEqual(
      standardGraphOptions.map(({ id }) => [id, 'database']),
    )
    expect(serviceMocks.updateNode).toHaveBeenNthCalledWith(
      1,
      'stress-balanced-100',
      'n-00099',
      { priorOdds: 4.5 },
      benchmarkSet.id,
    )
    expect(serviceMocks.updateNode).toHaveBeenNthCalledWith(
      12,
      'stress-shared-diamond-100k',
      'n-99999',
      { priorOdds: 4.5 },
      benchmarkSet.id,
    )
    expect(serviceMocks.getGraphCatalog).not.toHaveBeenCalled()
    expect(sequence.some((entry) => entry.startsWith('stress-deep-'))).toBe(false)
    expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(2)
    expect(onGraphUpdated).toHaveBeenCalledOnce()
    expect(screen.getByRole('tab', { name: 'Run' })).toHaveAttribute('aria-selected', 'true')
  })

  it('continues the stress suite after an exhaustive search reaches its time budget', async () => {
    serviceMocks.getBoundedNodeCounterSet.mockResolvedValueOnce({
      counterNodeIds: null,
      proofStatus: 'notProven',
      runNumber: 2,
      status: 'timedOut',
      stopReason: 'timeBudget',
      timeBudgetMilliseconds: 120_000,
    })
    serviceMocks.getGraphBySlug.mockResolvedValue({
      ...graph,
      slug: 'stress-balanced-1k',
      nodes: [
        { ...graph.nodes[0], id: 'n-00000' },
        { ...graph.nodes[1], id: 'n-00999' },
      ],
    })

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        installedStressGraphIds={['stress-balanced-1k']}
        isOpen
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    fireEvent.click(screen.getByRole('button', { name: 'Run standard stress suite' }))

    expect(await screen.findByText('Standard stress suite complete')).toBeInTheDocument()
    expect(screen.getByText(/5 requests completed · 1 exhaustive search reached the time budget/)).toBeInTheDocument()
    expect(screen.getByText(/6 of 6 attempted/)).toBeInTheDocument()
    expect(serviceMocks.getEvidenceImpactRanking).toHaveBeenCalledOnce()
    expect(screen.queryByText(/request failed/)).not.toBeInTheDocument()
  })

  it('stops the stress suite by interrupting a cancellable request and launches nothing else', async () => {
    let receivedSignal: AbortSignal | undefined
    serviceMocks.getNodeCounterSet.mockImplementation((...args: unknown[]) => {
      receivedSignal = args[3] as AbortSignal
      return new Promise((_, reject) => {
        receivedSignal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
      })
    })
    const interruptedRun = {
      runNumber: 1,
      benchmarkSetId: benchmarkSet.id,
      algorithm: { name: 'minimal-counter-set', implementation: 'greedy' },
      graph: { slug: 'stress-balanced-1k' },
      invocation: { dataSource: 'database', targetNodeId: 'n-00000' },
      outcome: { status: 'cancelled' },
      details: {},
    }
    serviceMocks.getPerformanceRuns
      .mockResolvedValueOnce(emptyReport())
      .mockResolvedValueOnce(report([interruptedRun]))

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        installedStressGraphIds={['stress-balanced-1k']}
        isOpen
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    fireEvent.click(screen.getByRole('button', { name: 'Run standard stress suite' }))

    const stopButton = await screen.findByRole('button', { name: 'Stop suite' })
    await waitFor(() => expect(serviceMocks.getNodeCounterSet).toHaveBeenCalledOnce())
    fireEvent.click(stopButton)

    expect(await screen.findByText('Standard stress suite stopped')).toBeInTheDocument()
    expect(screen.getByText(/1 request interrupted/)).toBeInTheDocument()
    expect(screen.getByText('Standard stress suite stopped').closest('[role="status"]')).toHaveFocus()
    expect(receivedSignal?.aborted).toBe(true)
    expect(serviceMocks.getBoundedNodeCounterSet).not.toHaveBeenCalled()
    expect(serviceMocks.getGraphBySlug).not.toHaveBeenCalled()
    expect(screen.getByRole('tab', { name: 'Run' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.queryByRole('article', { name: /Report for run/ })).not.toBeInTheDocument()
  })

  it('discovers installed stress graphs and stops after a non-cancellable request finishes', async () => {
    let finishEvidenceRun!: () => void
    const evidenceRun = new Promise<void>((resolve) => {
      finishEvidenceRun = resolve
    })
    serviceMocks.getGraphCatalog.mockResolvedValue([
      {
        slug: 'stress-balanced-1k',
        title: 'Balanced tree',
        description: null,
        nodeCount: 1000,
        edgeCount: 999,
      },
      {
        slug: 'sample-medium',
        title: 'Sample',
        description: null,
        nodeCount: 20,
        edgeCount: 19,
      },
    ])
    serviceMocks.getEvidenceImpactRanking.mockImplementation(async () => {
      await evidenceRun
      return { supportingEvidence: [], counterEvidence: [] }
    })

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    fireEvent.click(screen.getByRole('button', { name: 'Run standard stress suite' }))

    expect(await screen.findByText(/3 of 6 · Balanced tree \(1,000 nodes\) · Evidence impact ranking/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Stop suite' }))
    expect(screen.getByText('Stopping standard stress suite…')).toBeInTheDocument()
    expect(serviceMocks.getLeastRobustNode).not.toHaveBeenCalled()

    finishEvidenceRun()

    expect(await screen.findByText('Standard stress suite stopped')).toBeInTheDocument()
    expect(screen.getByText(/3 requests completed/)).toBeInTheDocument()
    expect(screen.getByText(/3 of 6 attempted/)).toBeInTheDocument()
    expect(serviceMocks.getLeastRobustNode).not.toHaveBeenCalled()
    expect(serviceMocks.getGraphBySlug).not.toHaveBeenCalled()
    expect(serviceMocks.getGraphCatalog).toHaveBeenCalledOnce()
  })

  it('excludes deep-chain graphs discovered from the catalog', async () => {
    serviceMocks.getGraphCatalog.mockResolvedValue([
      {
        slug: 'stress-deep-1k',
        title: 'Deep chain',
        description: null,
        nodeCount: 1000,
        edgeCount: 999,
      },
    ])

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    fireEvent.click(screen.getByRole('button', { name: 'Run standard stress suite' }))

    expect(await screen.findByText('No standard stress graphs found')).toBeInTheDocument()
    expect(screen.getByText(/Install at least one balanced, wide, or shared-diamond database stress graph/)).toBeInTheDocument()
    expect(serviceMocks.getGraphCatalog).toHaveBeenCalledOnce()
    expect(serviceMocks.getNodeCounterSet).not.toHaveBeenCalled()
  })

  it('continues after a suite request fails and identifies the graph and operation', async () => {
    serviceMocks.getNodeCounterSet.mockRejectedValueOnce(new Error('Request failed'))

    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        installedStressGraphIds={['stress-balanced-1k']}
        isOpen
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    fireEvent.click(screen.getByRole('button', { name: 'Run standard stress suite' }))

    expect(await screen.findByText('Standard stress suite complete')).toBeInTheDocument()
    expect(screen.getByText(/5 requests completed · 1 request failed/)).toBeInTheDocument()
    expect(serviceMocks.getBoundedNodeCounterSet).toHaveBeenCalledOnce()

    fireEvent.click(screen.getByText('Review failed requests'))
    expect(screen.getByText('Balanced tree (1,000 nodes) · Minimal counter set')).toBeInTheDocument()
  })

  it('stops launching suite work when the dialog unmounts', async () => {
    let receivedSignal: AbortSignal | undefined
    serviceMocks.getNodeCounterSet.mockImplementation((...args: unknown[]) => {
      receivedSignal = args[3] as AbortSignal
      return new Promise((_, reject) => {
        receivedSignal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
      })
    })

    const view = render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        installedStressGraphIds={['stress-balanced-1k']}
        isOpen
        onClose={vi.fn()}
      />,
    )
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    fireEvent.click(screen.getByRole('button', { name: 'Run standard stress suite' }))
    await waitFor(() => expect(serviceMocks.getNodeCounterSet).toHaveBeenCalledOnce())

    view.unmount()

    expect(receivedSignal?.aborted).toBe(true)
    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledTimes(2))
    expect(serviceMocks.getBoundedNodeCounterSet).not.toHaveBeenCalled()
  })

  it('skips leaf updates in fixture mode', async () => {
    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="fixture"
        isOpen
        onClose={vi.fn()}
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
    expect(within(operationCard(/^Minimal counter set$/)).getByRole('button', {
      name: 'Run Minimal counter set',
    })).toBeDisabled()
    expect(screen.getAllByText('This graph has no root node for this algorithm.')).toHaveLength(3)
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

    fireEvent.keyDown(screen.getByRole('tab', { name: /History/ }), { key: 'End' })
    expect(screen.getByRole('tab', { name: 'Trends' })).toHaveFocus()
    expect(screen.getByRole('tab', { name: 'Trends' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tabpanel', { name: 'Trends' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Historical trends' })).toBeInTheDocument()

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
        />
      </>,
    )

    expect(opener).not.toHaveAttribute('inert')
    expect(opener).not.toHaveAttribute('aria-hidden')
    expect(opener).toHaveFocus()
  })

  it('keeps keyboard focus inside an empty History panel', async () => {
    render(
      <InsightsLabDialog
        graph={graph}
        graphDataSource="database"
        isOpen
        onClose={vi.fn()}
      />,
    )

    await waitFor(() => expect(serviceMocks.getPerformanceRuns).toHaveBeenCalledOnce())
    const closeButton = screen.getByRole('button', { name: 'Close Insights Lab' })
    const historyTab = screen.getByRole('tab', { name: /History/ })
    fireEvent.click(historyTab)
    historyTab.focus()

    fireEvent.keyDown(historyTab, { key: 'Tab' })
    expect(closeButton).toHaveFocus()

    fireEvent.keyDown(closeButton, { key: 'Tab', shiftKey: true })
    expect(historyTab).toHaveFocus()
  })
})
